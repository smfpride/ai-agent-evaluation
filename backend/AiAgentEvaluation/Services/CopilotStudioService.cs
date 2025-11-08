using AiAgentEvaluation.Models;
using AiAgentEvaluation.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Implementation of Copilot Studio service using Direct Line API 3.0 with token endpoint
/// This version gets Direct Line tokens from a Copilot Studio token endpoint
/// </summary>
public class CopilotStudioService : ICopilotStudioService, IDisposable
{
    private readonly ILogger<CopilotStudioService> _logger;
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly HttpClient _tokenHttpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResilientService _resilientService;
    private bool _disposed;
    
    // Cache token until it expires (with some buffer time)
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly object _tokenLock = new object();

    public CopilotStudioService(
        ILogger<CopilotStudioService> logger, 
        IOptions<AppSettings> settings, 
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _settings = settings.Value;
        _httpClient = httpClient;
        
        // Create a separate HttpClient for token requests (without auth headers)
        _tokenHttpClient = httpClientFactory.CreateClient();
        
        // Initialize resilient service
        _resilientService = new ResilientService(_logger, settings);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Configure HttpClient for Direct Line API (without auth - we'll set it per request)
        _httpClient.BaseAddress = new Uri(_settings.CopilotStudio.DirectLineEndpoint);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.CopilotStudio.TimeoutSeconds);
    }

    /// <summary>
    /// Get a valid Direct Line token, using cached token if still valid or fetching a new one
    /// </summary>
    private async Task<string> GetValidTokenAsync(CancellationToken cancellationToken = default)
    {
        lock (_tokenLock)
        {
            // Return cached token if still valid (with 5-minute buffer)
            if (!string.IsNullOrEmpty(_cachedToken) && 
                _tokenExpiry > DateTime.UtcNow.AddMinutes(5))
            {
                return _cachedToken;
            }
        }

        // Need to fetch a new token
        return await FetchNewTokenAsync(cancellationToken);
    }

    /// <summary>
    /// Fetch a new token from the Copilot Studio token endpoint
    /// </summary>
    private async Task<string> FetchNewTokenAsync(CancellationToken cancellationToken = default)
    {
        var tokenEndpoint = _settings.CopilotStudio.TokenEndpoint;
        
        if (string.IsNullOrEmpty(tokenEndpoint))
        {
            throw new InvalidOperationException("Copilot Studio token endpoint is not configured. Set CopilotStudio:TokenEndpoint to your token endpoint URL.");
        }

        _logger.LogDebug("Fetching new Direct Line token from endpoint");

        var response = await _tokenHttpClient.GetAsync(tokenEndpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokenResponse = JsonSerializer.Deserialize<CopilotStudioTokenResponse>(content, _jsonOptions);

        if (string.IsNullOrEmpty(tokenResponse?.Token))
        {
            throw new InvalidOperationException("Failed to get Direct Line token - no token returned from endpoint");
        }

        lock (_tokenLock)
        {
            _cachedToken = tokenResponse.Token;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        }

        _logger.LogDebug("Successfully obtained Direct Line token, expires in {ExpiresIn} seconds", tokenResponse.ExpiresIn);
        return tokenResponse.Token;
    }

    /// <summary>
    /// Create an authenticated HttpClient with current token
    /// </summary>
    private async Task<HttpClient> GetAuthenticatedHttpClientAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetValidTokenAsync(cancellationToken);
        
        // Clear any existing auth headers
        _httpClient.DefaultRequestHeaders.Authorization = null;
        
        // Set the Bearer token
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        
        return _httpClient;
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

        var httpClient = await GetAuthenticatedHttpClientAsync(cancellationToken);
        var response = await httpClient.PostAsync($"/v3/directline/conversations/{conversationId}/activities", content, cancellationToken);
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

                var httpClient = await GetAuthenticatedHttpClientAsync(cancellationToken);
                var response = await httpClient.GetAsync(url, cancellationToken);
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
            
            var httpClient = await GetAuthenticatedHttpClientAsync(ct);
            var response = await httpClient.PostAsync("/v3/directline/conversations", null, ct);
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

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_httpClient == null)
            {
                _logger.LogWarning("Copilot Studio HTTP client is not initialized");
                return false;
            }

            // Try to get a token to verify the token endpoint works
            var token = await GetValidTokenAsync(cancellationToken);
            
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain Direct Line token for health check");
                return false;
            }

            // Verify that we can reach the Direct Line API endpoint with the token
            var httpClient = await GetAuthenticatedHttpClientAsync(cancellationToken);
            var response = await httpClient.GetAsync("/v3/directline", cancellationToken);
            
            // Accept any response (including 405 Method Not Allowed) as long as we can connect
            var isHealthy = response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable;
            
            _logger.LogDebug("Copilot Studio health check {Status}", isHealthy ? "passed" : "failed");
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot Studio health check failed");
            return false;
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
            _tokenHttpClient?.Dispose();
            // HttpClient is typically managed by DI container, so we don't dispose it here
            _disposed = true;
        }
    }
}