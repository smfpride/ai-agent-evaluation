using AiAgentEvaluation.Models;
using AiAgentEvaluation.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Direct Line API conversation response
/// </summary>
public class DirectLineConversationResponse
{
    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>
/// Direct Line API activity message
/// </summary>
public class DirectLineActivity
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("from")]
    public DirectLineFrom From { get; set; } = new();

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("channelId")]
    public string ChannelId { get; set; } = "directline";
}

/// <summary>
/// Direct Line API from/to identity
/// </summary>
public class DirectLineFrom
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "user";

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Direct Line API activities response
/// </summary>
public class DirectLineActivitiesResponse
{
    [JsonPropertyName("activities")]
    public DirectLineActivity[] Activities { get; set; } = Array.Empty<DirectLineActivity>();

    [JsonPropertyName("watermark")]
    public string? Watermark { get; set; }
}

/// <summary>
/// Implementation of Copilot Studio service using Direct Line API 3.0
/// </summary>
public class CopilotStudioService : ICopilotStudioService, IDisposable
{
    private readonly ILogger<CopilotStudioService> _logger;
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResilientService _resilientService;
    private bool _disposed;

    public CopilotStudioService(ILogger<CopilotStudioService> logger, IOptions<AppSettings> settings, HttpClient httpClient)
    {
        _logger = logger;
        _settings = settings.Value;
        _httpClient = httpClient;
        
        // Initialize resilient service
        _resilientService = new ResilientService(_logger, settings);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Configure HttpClient for Direct Line API
        _httpClient.BaseAddress = new Uri(_settings.CopilotStudio.DirectLineEndpoint);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.CopilotStudio.DirectLineSecret}");
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.CopilotStudio.TimeoutSeconds);
    }

    public async Task<CopilotDirectLineResponse> SendMessageAsync(string agentId, string userInput, string? conversationId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending message to agent {AgentId} in conversation {ConversationId}", agentId, conversationId);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var actualConversationId = conversationId;

        return await _resilientService.ExecuteAsync(async ct =>
        {
            try
            {
                // Start conversation if not provided
                if (string.IsNullOrEmpty(actualConversationId))
                {
                    actualConversationId = await StartConversationAsync(agentId, ct);
                }

                // Send message to bot
                await SendActivityAsync(actualConversationId, userInput, ct);
                
                // Wait for response from bot
                var response = await WaitForBotResponseAsync(actualConversationId, ct);
                
                stopwatch.Stop();
                
                return new CopilotDirectLineResponse
                {
                    ConversationId = actualConversationId,
                    Response = response ?? string.Empty,
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Success = !string.IsNullOrEmpty(response)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to agent {AgentId} in conversation {ConversationId}", 
                    agentId, actualConversationId);
                stopwatch.Stop();
                
                return new CopilotDirectLineResponse
                {
                    ConversationId = actualConversationId ?? string.Empty,
                    Response = string.Empty,
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }, "SendMessage",
        exception => exception is HttpRequestException || exception is TaskCanceledException,
        cancellationToken);
    }

    private async Task SendActivityAsync(string conversationId, string text, CancellationToken cancellationToken)
    {
        var activity = new DirectLineActivity
        {
            Type = "message",
            From = new DirectLineFrom { Id = "user" },
            Text = text,
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(activity, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"/v3/directline/conversations/{conversationId}/activities", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        _logger.LogDebug("Sent activity to conversation {ConversationId}", conversationId);
    }

    private async Task<string?> WaitForBotResponseAsync(string conversationId, CancellationToken cancellationToken)
    {
        var maxAttempts = 30; // 30 attempts with 1-second intervals = 30 seconds max wait
        var attempt = 0;
        string? watermark = null;

        while (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var url = $"/v3/directline/conversations/{conversationId}/activities";
                if (!string.IsNullOrEmpty(watermark))
                {
                    url += $"?watermark={watermark}";
                }

                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var activitiesResponse = JsonSerializer.Deserialize<DirectLineActivitiesResponse>(content, _jsonOptions);

                if (activitiesResponse?.Activities != null)
                {
                    watermark = activitiesResponse.Watermark;
                    
                    // Look for bot responses (not from user)
                    var botMessages = activitiesResponse.Activities
                        .Where(a => a.Type == "message" && a.From.Id != "user" && !string.IsNullOrEmpty(a.Text))
                        .ToList();

                    if (botMessages.Any())
                    {
                        // Return the latest bot message
                        var latestMessage = botMessages.Last();
                        _logger.LogDebug("Received bot response in conversation {ConversationId}: {Response}", 
                            conversationId, latestMessage.Text);
                        return latestMessage.Text;
                    }
                }

                // Wait before next attempt
                await Task.Delay(1000, cancellationToken);
                attempt++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error polling for bot response in conversation {ConversationId}, attempt {Attempt}", 
                    conversationId, attempt);
                await Task.Delay(1000, cancellationToken);
                attempt++;
            }
        }

        _logger.LogWarning("Timeout waiting for bot response in conversation {ConversationId} after {Attempts} attempts", 
            conversationId, attempt);
        return null;
    }

    public async Task<string> StartConversationAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return await _resilientService.ExecuteAsync(async (ct) =>
        {
            _logger.LogInformation("Starting conversation with agent {AgentId}", agentId);
            
            var response = await _httpClient.PostAsync("/v3/directline/conversations", null, ct);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(ct);
            var conversationResponse = JsonSerializer.Deserialize<DirectLineConversationResponse>(content, _jsonOptions);
            
            if (conversationResponse?.ConversationId == null)
            {
                throw new InvalidOperationException("Failed to create conversation - no conversation ID returned");
            }
            
            _logger.LogInformation("Started conversation {ConversationId} with agent {AgentId}", 
                conversationResponse.ConversationId, agentId);
            
            return conversationResponse.ConversationId;
        }, "StartConversation",
        exception => exception is HttpRequestException || exception is TaskCanceledException,
        cancellationToken);
    }

    public async Task EndConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Ending conversation {ConversationId}", conversationId);
        
        try
        {
            // Direct Line API doesn't have an explicit end conversation endpoint
            // Conversations automatically expire after a period of inactivity
            // We can optionally send a farewell message or just log the end
            
            _logger.LogDebug("Conversation {ConversationId} marked as ended (will expire automatically)", conversationId);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending conversation {ConversationId}", conversationId);
            throw;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // HttpClient is typically managed by DI container, so we don't dispose it here
            _disposed = true;
        }
    }
}