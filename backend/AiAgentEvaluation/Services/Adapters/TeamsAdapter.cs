using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for Microsoft Teams bots using Bot Framework Direct Line API
/// Configuration: { "botId": "...", "tenantId": "...", "environmentVariables": { "appId": "...", "appSecret": "..." } }
/// </summary>
public class TeamsAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    private const string DirectLineEndpoint = "https://directline.botframework.com/v3/directline";
    private const string TokenEndpoint = "https://login.microsoftonline.com/botframework.com/oauth2/v2.0/token";
    
    public TeamsAdapter(
        ILogger<TeamsAdapter> logger,
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
            var botId = request.Configuration.GetValue<string>("botId");
            
            // Validate required fields
            if (string.IsNullOrEmpty(botId))
            {
                throw new InvalidOperationException(
                    "Teams adapter requires 'botId' in configuration");
            }
            
            Logger.LogInformation(
                "TeamsAdapter: Calling Teams bot {BotId} for test case {TestCaseId}",
                botId, request.TestCaseId);
            
            // Get all credentials from Dataverse environment variables
            var credentials = await GetAllCredentialsFromEnvironmentVariablesAsync(
                request.Configuration,
                cancellationToken);
            
            var appId = credentials["appId"];
            var appSecret = credentials["appSecret"];
            
            // Get Bot Framework token
            var token = await GetBotFrameworkTokenAsync(appId, appSecret, cancellationToken);
            
            // Start conversation with bot
            var conversationId = await StartConversationAsync(token, cancellationToken);
            
            // Send message to bot
            var botResponse = await SendMessageToBotAsync(
                conversationId, 
                request.UserInput, 
                token,
                request,
                cancellationToken);
            
            return new AgentResponse
            {
                Success = true,
                ResponseText = botResponse,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds,
                ConversationId = conversationId
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "TeamsAdapter: Failed for test case {TestCaseId}", 
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
    
    private async Task<string> GetBotFrameworkTokenAsync(string appId, string appSecret, CancellationToken ct)
    {
        try
        {
            using (var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", appId),
                new KeyValuePair<string, string>("client_secret", appSecret),
                new KeyValuePair<string, string>("scope", "https://api.botframework.com/.default")
            }))
            {
                var response = await _httpClient.PostAsync(TokenEndpoint, content, ct);
                response.EnsureSuccessStatusCode();
                
                var responseContent = await response.Content.ReadAsStringAsync(ct);
                var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                
                return tokenResponse.GetProperty("access_token").GetString() 
                    ?? throw new InvalidOperationException("Failed to get access token from response");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to acquire Bot Framework token");
            throw new InvalidOperationException("Failed to acquire Bot Framework token", ex);
        }
    }
    
    private async Task<string> StartConversationAsync(string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DirectLineEndpoint}/conversations");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            
            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var conversationResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            return conversationResponse.GetProperty("conversationId").GetString()
                ?? throw new InvalidOperationException("Failed to get conversation ID from response");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to start conversation");
            throw new InvalidOperationException("Failed to start conversation with bot", ex);
        }
    }
    
    private async Task<string> SendMessageToBotAsync(
        string conversationId, 
        string message, 
        string token,
        AgentCallRequest request,
        CancellationToken ct)
    {
        try
        {
            // Send activity
            var userId = request.Configuration.GetValue<string>("userId") 
                ?? $"test-user-{request.TestCaseId}";
            
            var activity = new
            {
                type = "message",
                from = new { id = userId },
                text = message
            };
            
            var activityJson = JsonSerializer.Serialize(activity);
            
            using (var postRequest = new HttpRequestMessage(HttpMethod.Post, 
                $"{DirectLineEndpoint}/conversations/{conversationId}/activities"))
            {
                postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                postRequest.Content = new StringContent(activityJson, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.SendAsync(postRequest, ct);
                response.EnsureSuccessStatusCode();
            }
            
            // Wait for bot response (poll for activities)
            await Task.Delay(2000, ct); // Give bot time to respond
            
            using (var getRequest = new HttpRequestMessage(HttpMethod.Get, 
                $"{DirectLineEndpoint}/conversations/{conversationId}/activities"))
            {
                getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                
                var getResponse = await _httpClient.SendAsync(getRequest, ct);
                getResponse.EnsureSuccessStatusCode();
                
                var activitiesContent = await getResponse.Content.ReadAsStringAsync(ct);
                var activitiesResponse = JsonSerializer.Deserialize<JsonElement>(activitiesContent);
                
                // Find bot's response (skip user's message)
                var activities = activitiesResponse.GetProperty("activities");
                var botActivities = activities.EnumerateArray()
                    .Where(act => act.TryGetProperty("from", out var from) && 
                                  from.TryGetProperty("id", out var fromId) &&
                                  fromId.GetString() != userId &&
                                  act.TryGetProperty("text", out var _));
                
                foreach (var act in botActivities)
                {
                    if (act.TryGetProperty("text", out var text))
                    {
                        return text.GetString() ?? string.Empty;
                    }
                }
            }
            Logger.LogWarning("No bot response found in conversation {ConversationId}", conversationId);
            return string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send message and get response");
            throw new InvalidOperationException("Failed to send message and get response from bot", ex);
        }
    }
}
