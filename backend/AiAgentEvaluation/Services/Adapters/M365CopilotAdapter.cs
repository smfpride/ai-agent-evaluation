using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for M365 Copilot using Microsoft Graph API
/// Supports ManagedIdentity, UserCredentials, and ClientCredentials authentication modes
/// Configuration: { "authMode": "...", "tenantId": "...", "clientId": "...", "scopes": [...], "environmentVariables": {...} }
/// </summary>
public class M365CopilotAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    
    public M365CopilotAdapter(
        ILogger<M365CopilotAdapter> logger,
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
            var tenantId = request.Configuration.GetValue<string>("tenantId");
            var scopes = request.Configuration.GetValue<string[]>("scopes") ?? 
                new[] { "https://graph.microsoft.com/.default" };
            
            Logger.LogInformation(
                "M365CopilotAdapter: Calling M365 Copilot for test case {TestCaseId} with auth mode {AuthMode}",
                request.TestCaseId,
                request.Configuration.GetValue<string>("authMode") ?? "ManagedIdentity");
            
            // Get appropriate credential based on auth mode
            var credential = await GetAzureCredentialAsync(
                request.Configuration,
                scopes,
                cancellationToken);
            
            // Create Graph client with chosen credential
            var graphClient = new GraphServiceClient(credential, scopes);
            
            // For M365 Copilot, we'll use Microsoft Graph Chat API
            // Note: As of now, M365 Copilot doesn't have a direct API, so we simulate the interaction
            // using Graph Chat API or fallback to a message-based approach
            
            var userId = request.Configuration.GetValue<string>("userId") ?? "me";
            
            // Try to send a chat message (this is a placeholder - actual M365 Copilot API may differ)
            var chatMessage = new ChatMessage
            {
                Body = new ItemBody
                {
                    Content = request.UserInput,
                    ContentType = BodyType.Text
                }
            };
            
            // Since M365 Copilot API is evolving, we'll simulate by creating a chat or sending to a specific endpoint
            // In production, this would use the actual M365 Copilot API endpoint
            var responseText = await SimulateM365CopilotInteractionAsync(
                graphClient, 
                request.UserInput, 
                userId,
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
                "M365CopilotAdapter: Failed for test case {TestCaseId}", 
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
    
    private async Task<string> SimulateM365CopilotInteractionAsync(
        GraphServiceClient graphClient,
        string userInput,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Note: M365 Copilot API is still evolving. This is a placeholder implementation
            // that demonstrates the pattern. In production, replace with actual M365 Copilot endpoints
            
            // Option 1: Try to use Graph Search API (closest to Copilot functionality)
            // Option 2: Use Teams Chat API to simulate conversation
            // Option 3: Use a custom endpoint when M365 Copilot API becomes available
            
            // For now, we'll create a simple response indicating the adapter is ready
            // but waiting for the official M365 Copilot API
            
            Logger.LogInformation("M365 Copilot interaction simulated for user {UserId}", userId);
            
            // In production, this would be something like:
            // var response = await graphClient.Me.Copilot.Chat.PostAsync(new CopilotChatRequest { Message = userInput });
            
            return $"M365 Copilot response to: {userInput}. " +
                   $"(Note: Using Graph API with {graphClient.RequestAdapter.GetType().Name}. " +
                   $"Replace with actual M365 Copilot API when available.)";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to interact with M365 Copilot via Graph API");
            throw new InvalidOperationException("Failed to interact with M365 Copilot", ex);
        }
    }
}
