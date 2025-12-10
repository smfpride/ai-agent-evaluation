using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for M365 Agents SDK
/// Configuration: { "agentId": "...", "endpoint": "..." }
/// Uses Managed Identity for authentication
/// </summary>
public class M365AgentsSDKAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    private const string DefaultEndpoint = "https://agents.microsoft.com/api";
    
    public M365AgentsSDKAdapter(
        ILogger<M365AgentsSDKAdapter> logger,
        HttpClient httpClient,
        IDataverseService dataverseService)
        : base(logger, dataverseService)
    {
        _httpClient = httpClient;
    }
    
    public override async Task<AgentResponse> ExecuteAsync(
        AgentCallRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // Get configuration
            var agentId = request.Configuration.GetValue<string>("agentId");
            var endpoint = request.Configuration.GetValue<string>("endpoint") ?? DefaultEndpoint;
            
            // Validate required fields
            if (string.IsNullOrEmpty(agentId))
            {
                throw new InvalidOperationException(
                    "M365 Agents SDK adapter requires 'agentId' in configuration");
            }
            
            Logger.LogInformation(
                "M365AgentsSDKAdapter: Calling agent {AgentId} at {Endpoint} for test case {TestCaseId}",
                agentId, endpoint, request.TestCaseId);
            
            // Use Managed Identity for authentication
            var credential = new DefaultAzureCredential();
            
            // Get access token for M365 Agents API
            var tokenRequestContext = new Azure.Core.TokenRequestContext(
                new[] { $"{endpoint}/.default" });
            var token = await credential.GetTokenAsync(tokenRequestContext, cancellationToken);
            
            // Call M365 Agents SDK endpoint
            var responseText = await CallM365AgentAsync(
                endpoint,
                agentId,
                request.UserInput,
                token.Token,
                cancellationToken);
            
            return new AgentResponse
            {
                Success = true,
                ResponseText = responseText,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "M365AgentsSDKAdapter: Failed for test case {TestCaseId}", 
                request.TestCaseId);
            
            return new AgentResponse
            {
                Success = false,
                ResponseText = string.Empty,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
    
    private async Task<string> CallM365AgentAsync(
        string endpoint,
        string agentId,
        string userInput,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            // Construct M365 Agents SDK API endpoint
            var agentEndpoint = $"{endpoint}/agents/{agentId}/invoke";
            
            // Create request payload
            var requestPayload = new
            {
                input = userInput,
                context = new
                {
                    conversationId = Guid.NewGuid().ToString()
                }
            };
            
            var jsonContent = JsonSerializer.Serialize(requestPayload);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, agentEndpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Content = content;
            
            Logger.LogDebug("Sending request to M365 Agent endpoint: {Endpoint}", agentEndpoint);
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.LogWarning("M365 Agent call failed with status {StatusCode}: {Error}", 
                    response.StatusCode, errorContent);
                
                // Throw exception to propagate actual API error
                throw new InvalidOperationException(
                    $"M365 Agent call failed with status {response.StatusCode}: {errorContent}");
            }
            
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            // Parse response based on M365 Agents SDK response format
            if (responseObj.TryGetProperty("output", out var output))
            {
                return output.GetString() ?? string.Empty;
            }
            
            if (responseObj.TryGetProperty("response", out var resp))
            {
                return resp.GetString() ?? string.Empty;
            }
            
            return responseContent;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to call M365 Agent via SDK");
            throw new InvalidOperationException("Failed to call M365 Agent", ex);
        }
    }
}
