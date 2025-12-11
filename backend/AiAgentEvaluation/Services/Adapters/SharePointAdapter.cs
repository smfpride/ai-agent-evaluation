using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for SharePoint Embedded Agents
/// Configuration: { "authMode": "...", "siteUrl": "...", "agentId": "...", "tenantId": "...", "clientId": "...", "environmentVariables": {...} }
/// </summary>
public class SharePointAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    
    public SharePointAdapter(
        ILogger<SharePointAdapter> logger,
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
            var siteUrl = request.Configuration.GetValue<string>("siteUrl");
            var agentId = request.Configuration.GetValue<string>("agentId");
            
            // Validate required fields
            if (string.IsNullOrEmpty(siteUrl))
            {
                throw new InvalidOperationException(
                    "SharePoint adapter requires 'siteUrl' in configuration");
            }
            
            if (string.IsNullOrEmpty(agentId))
            {
                throw new InvalidOperationException(
                    "SharePoint adapter requires 'agentId' in configuration");
            }
            
            Logger.LogInformation(
                "SharePointAdapter: Calling SharePoint agent {AgentId} on site {SiteUrl} for test case {TestCaseId}",
                agentId, siteUrl, request.TestCaseId);
            
            var scopes = new[] { "https://graph.microsoft.com/.default" };
            
            // Get appropriate credential based on auth mode
            var credential = await GetAzureCredentialAsync(
                request.Configuration,
                scopes,
                cancellationToken);
            
            // Create Graph client for SharePoint access
            using (var graphClient = new GraphServiceClient(credential, scopes))
            {
                // Call SharePoint agent endpoint
                var responseText = await CallSharePointAgentAsync(
                    graphClient,
                    credential,
                    siteUrl,
                    agentId,
                    request.UserInput,
                    request.Configuration,
                    cancellationToken);

                return new AgentResponse
                {
                    Success = true,
                    ResponseText = responseText,
                    ExecutionTimeMs = (int)sw.ElapsedMilliseconds
                };
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "SharePointAdapter: Failed for test case {TestCaseId}", 
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
    
    private async Task<string> CallSharePointAgentAsync(
        GraphServiceClient graphClient,
        Azure.Core.TokenCredential credential,
        string siteUrl,
        string agentId,
        string userInput,
        AgentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            // Extract site information from URL
            var uri = new Uri(siteUrl);
            var hostName = uri.Host;
            var sitePath = uri.AbsolutePath;
            
            // Get site ID
            var site = await graphClient.Sites[hostName + ":" + sitePath].GetAsync(cancellationToken: cancellationToken);
            
            if (site == null)
            {
                throw new InvalidOperationException($"SharePoint site not found: {siteUrl}");
            }
            
            Logger.LogInformation("Accessed SharePoint site {SiteId}", site.Id);
            
            // SharePoint Embedded Agents API endpoint (this is a conceptual endpoint)
            // In production, replace with actual SharePoint Agents API
            var agentEndpoint = $"{siteUrl}/_api/agents/{agentId}/query";
            
            // Reuse the credential passed from the caller
            var scopes = new[] { "https://graph.microsoft.com/.default" };
            var tokenRequestContext = new Azure.Core.TokenRequestContext(scopes);
            var token = await credential.GetTokenAsync(tokenRequestContext, cancellationToken);
            
            // Create request to SharePoint agent
            var requestContent = new
            {
                query = userInput,
                agentId = agentId
            };
            
            var jsonContent = JsonSerializer.Serialize(requestContent);
            
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, agentEndpoint))
            {
                using (var stringContent = new StringContent(jsonContent, Encoding.UTF8, "application/json"))
                {
                    httpRequest.Content = stringContent;
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                    
                    var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        Logger.LogError("SharePoint agent call failed with status {StatusCode}: {Error}", 
                            response.StatusCode, errorContent);
                        throw new InvalidOperationException(
                            $"SharePoint agent call failed with status {response.StatusCode}: {errorContent}");
                    }
                    
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    if (responseObj.TryGetProperty("answer", out var answer))
                    {
                        return answer.GetString() ?? string.Empty;
                    }
                    
                    return responseContent;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to call SharePoint agent");
            throw new InvalidOperationException("Failed to call SharePoint agent", ex);
        }
    }
}
