using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for Slack bots using Slack Web API
/// Configuration: { "channelId": "...", "environmentVariables": { "botToken": "...", "appToken": "..." } }
/// </summary>
public class SlackAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    private const string SlackApiBase = "https://slack.com/api";
    
    public SlackAdapter(
        ILogger<SlackAdapter> logger,
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
            var channelId = request.Configuration.GetValue<string>("channelId");
            
            // Validate required fields
            if (string.IsNullOrEmpty(channelId))
            {
                throw new InvalidOperationException(
                    "Slack adapter requires 'channelId' in configuration");
            }
            
            Logger.LogInformation(
                "SlackAdapter: Sending message to Slack channel {ChannelId} for test case {TestCaseId}",
                channelId, request.TestCaseId);
            
            // Get all credentials from Dataverse environment variables
            var credentials = await GetAllCredentialsFromEnvironmentVariablesAsync(
                request.Configuration,
                cancellationToken);
            
            var botToken = credentials["botToken"];
            
            // Send message to Slack bot
            var messageTimestamp = await SendMessageToSlackAsync(
                botToken,
                channelId,
                request.UserInput,
                cancellationToken);
            
            // Wait for bot response
            var responseText = await WaitForBotResponseAsync(
                botToken,
                channelId,
                messageTimestamp,
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
                "SlackAdapter: Failed for test case {TestCaseId}", 
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
    
    private async Task<string> SendMessageToSlackAsync(
        string botToken,
        string channelId,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestPayload = new
            {
                channel = channelId,
                text = message
            };
            
            var jsonContent = JsonSerializer.Serialize(requestPayload);
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{SlackApiBase}/chat.postMessage");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", botToken);
            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            
            var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            if (!responseObj.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var error = responseObj.TryGetProperty("error", out var err) ? err.GetString() : "Unknown error";
                throw new InvalidOperationException($"Slack API error: {error}");
            }
            
            if (!responseObj.TryGetProperty("ts", out var timestamp))
            {
                throw new InvalidOperationException("Failed to get message timestamp from Slack response");
            }
            
            return timestamp.GetString() ?? throw new InvalidOperationException("Message timestamp is null");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send message to Slack");
            throw new InvalidOperationException("Failed to send message to Slack", ex);
        }
    }
    
    private async Task<string> WaitForBotResponseAsync(
        string botToken,
        string channelId,
        string afterTimestamp,
        CancellationToken cancellationToken)
    {
        try
        {
            // Poll for bot response with exponential backoff (max 30 seconds)
            var maxAttempts = 10;
            var delayMs = 1000;
            
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                await Task.Delay(delayMs, cancellationToken);
                
                // Get conversation history
                var url = $"{SlackApiBase}/conversations.history?channel={channelId}&oldest={afterTimestamp}&limit=10";
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", botToken);
                
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                
                var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
                
                if (!responseObj.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                {
                    var error = responseObj.TryGetProperty("error", out var err) ? err.GetString() : "Unknown error";
                    Logger.LogWarning("Slack conversations.history error: {Error}", error);
                    continue;
                }
                
                if (responseObj.TryGetProperty("messages", out var messages))
                {
                    foreach (var msg in messages.EnumerateArray())
                    {
                        // Look for bot messages (not from user, and after our message)
                        if (msg.TryGetProperty("bot_id", out _) && 
                            msg.TryGetProperty("text", out var text))
                        {
                            var messageText = text.GetString();
                            if (!string.IsNullOrEmpty(messageText))
                            {
                                Logger.LogInformation("Received bot response from Slack");
                                return messageText;
                            }
                        }
                    }
                }
                
                // Exponential backoff
                delayMs = Math.Min(delayMs * 2, 5000);
            }
            
            Logger.LogWarning("No bot response found in Slack channel {ChannelId} after {Attempts} attempts", 
                channelId, maxAttempts);
            return string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to wait for bot response from Slack");
            throw new InvalidOperationException("Failed to wait for bot response from Slack", ex);
        }
    }
}
