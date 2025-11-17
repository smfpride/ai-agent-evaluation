using AiAgentEvaluation.Models;
using AiAgentEvaluation.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Implementation of Copilot Studio service using Direct Line API 3.0 with dynamic token endpoint URLs
/// This version gets Direct Line tokens from Copilot Studio token endpoints specified per-request
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
    
    // Cache tokens per URL until they expire (with some buffer time)
    private readonly Dictionary<string, (string Token, DateTime Expiry)> _tokenCache = new();
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

        // Configure HttpClient timeout for Direct Line API
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.CopilotStudio.TimeoutSeconds);
    }

    /// <summary>
    /// Get a valid Direct Line token for a specific endpoint, using cached token if still valid or fetching a new one
    /// </summary>
    private async Task<string> GetValidTokenAsync(string directLineSpeechUrl, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh)
        {
            lock (_tokenLock)
            {
                // Return cached token if still valid (with 2-minute buffer to be safe)
                if (_tokenCache.TryGetValue(directLineSpeechUrl, out var cached) && 
                    cached.Expiry > DateTime.UtcNow.AddMinutes(2))
                {
                    _logger.LogDebug("Using cached token for {Endpoint}, expires at {Expiry}", 
                        directLineSpeechUrl, cached.Expiry);
                    return cached.Token;
                }
            }
        }
        else
        {
            _logger.LogInformation("Force refreshing token for {Endpoint}", directLineSpeechUrl);
        }

        // Need to fetch a new token
        return await FetchNewTokenAsync(directLineSpeechUrl, cancellationToken);
    }

    /// <summary>
    /// Fetch a new token from the Copilot Studio token endpoint URL with retry logic
    /// </summary>
    private async Task<string> FetchNewTokenAsync(string directLineSpeechUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(directLineSpeechUrl))
        {
            throw new InvalidOperationException("Direct Line Speech URL is required but was not provided.");
        }

        _logger.LogDebug("Fetching new Direct Line token from endpoint: {Endpoint}", directLineSpeechUrl);

        // Wrap token fetch in resilient service for automatic retries on transient failures
        return await _resilientService.ExecuteAsync(async ct =>
        {
            var response = await _tokenHttpClient.GetAsync(directLineSpeechUrl, ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);
            var tokenResponse = JsonSerializer.Deserialize<CopilotStudioTokenResponse>(content, _jsonOptions);

            if (string.IsNullOrEmpty(tokenResponse?.Token))
            {
                throw new InvalidOperationException("Failed to get Direct Line token - no token returned from endpoint");
            }

            lock (_tokenLock)
            {
                _tokenCache[directLineSpeechUrl] = (tokenResponse.Token, DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
            }

            _logger.LogDebug("Successfully obtained Direct Line token for {Endpoint}, expires in {ExpiresIn} seconds", 
                directLineSpeechUrl, tokenResponse.ExpiresIn);
            return tokenResponse.Token;
        }, "FetchDirectLineToken",
        exception => exception is HttpRequestException || exception is TaskCanceledException,
        cancellationToken);
    }

    /// <summary>
    /// Get authentication token for the specified Direct Line endpoint
    /// </summary>
    private async Task<string> GetAuthenticationTokenAsync(string directLineSpeechUrl, CancellationToken cancellationToken = default)
    {
        return await GetValidTokenAsync(directLineSpeechUrl, false, cancellationToken);
    }

    /// <summary>
    /// Create an HTTP request message with authentication header
    /// </summary>
    private async Task<HttpRequestMessage> CreateAuthenticatedRequestAsync(HttpMethod method, string directLineSpeechUrl, string fullUrl, HttpContent? content = null, bool forceRefreshToken = false, CancellationToken cancellationToken = default)
    {
        var token = await GetValidTokenAsync(directLineSpeechUrl, forceRefreshToken, cancellationToken);
        var request = new HttpRequestMessage(method, fullUrl);
        request.Headers.Add("Authorization", $"Bearer {token}");
        
        if (content != null)
        {
            request.Content = content;
        }
        
        return request;
    }

    public async Task<CopilotDirectLineResponse> SendMessageAsync(string directLineSpeechUrl, string userInput, string? conversationId = null, string? directLineToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending message in conversation {ConversationId} using endpoint {Endpoint}, tokenProvided={TokenProvided}, userInput={UserInput}", 
            conversationId, directLineSpeechUrl, !string.IsNullOrEmpty(directLineToken), userInput);
        
        // Extract Direct Line base URL from the token endpoint
        var directLineBaseUrl = ExtractDirectLineBaseUrl(directLineSpeechUrl);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var actualConversationId = conversationId;
        var actualDirectLineToken = directLineToken;

        return await _resilientService.ExecuteAsync(async ct =>
        {
            try
            {
                // Start conversation if not provided
                if (string.IsNullOrEmpty(actualConversationId))
                {
                    var (convId, token) = await StartConversationInternalAsync(directLineSpeechUrl, ct);
                    actualConversationId = convId;
                    actualDirectLineToken = token;
                }

                // Send message to bot and capture timestamp
                var sentTimestamp = await SendActivityAsync(directLineSpeechUrl, directLineBaseUrl, actualConversationId, userInput, actualDirectLineToken, ct);
                
                // Wait for response from bot, passing the sent timestamp and user input for filtering
                var (responseText, rawActivityJson) = await WaitForBotResponseAsync(directLineSpeechUrl, directLineBaseUrl, actualConversationId, sentTimestamp, userInput, actualDirectLineToken, ct);
                
                stopwatch.Stop();
                
                return new CopilotDirectLineResponse
                {
                    ConversationId = actualConversationId,
                    Response = responseText ?? string.Empty,
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Success = !string.IsNullOrEmpty(responseText),
                    RawActivityJson = rawActivityJson,
                    DirectLineToken = actualDirectLineToken
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message in conversation {ConversationId}", 
                    actualConversationId);
                stopwatch.Stop();
                
                return new CopilotDirectLineResponse
                {
                    ConversationId = actualConversationId ?? string.Empty,
                    Response = string.Empty,
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Success = false,
                    ErrorMessage = ex.Message,
                    DirectLineToken = actualDirectLineToken
                };
            }
        }, "SendMessage",
        exception => exception is HttpRequestException || exception is TaskCanceledException,
        cancellationToken);
    }

    /// <summary>
    /// Extract the Direct Line base URL from the token endpoint URL
    /// Typically the token endpoint is like: https://xxx.powerplatform.com/powervirtualagents/.../directline/token
    /// And the Direct Line base URL is: https://directline.botframework.com
    /// </summary>
    private string ExtractDirectLineBaseUrl(string directLineSpeechUrl)
    {
        // Default to the standard Direct Line endpoint
        return "https://directline.botframework.com";
    }

    private async Task<DateTime> SendActivityAsync(string directLineSpeechUrl, string directLineBaseUrl, string conversationId, string text, string? directLineToken, CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var activity = new DirectLineActivity
        {
            Type = "message",
            From = new DirectLineFrom { Id = "user" },  // Explicitly set user ID
            Text = text,
            Timestamp = timestamp,
            Locale = "en-US"  // Add locale to avoid authentication issues
        };

        var json = JsonSerializer.Serialize(activity, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        _logger.LogWarning("SendActivityAsync: text parameter='{Text}', activity.Text='{ActivityText}', json={Json}", 
            text, activity.Text, json);

        var fullUrl = $"{directLineBaseUrl}/v3/directline/conversations/{conversationId}/activities";
        
        HttpRequestMessage request;
        if (!string.IsNullOrEmpty(directLineToken))
        {
            // Use the provided conversation-specific token
            _logger.LogDebug("Using provided DirectLine token for conversation {ConversationId}", conversationId);
            request = new HttpRequestMessage(HttpMethod.Post, fullUrl);
            request.Headers.Add("Authorization", $"Bearer {directLineToken}");
            request.Content = content;
        }
        else
        {
            // Fall back to fetching a token from Copilot Studio (should not happen in multi-turn)
            _logger.LogWarning("No DirectLine token provided for existing conversation {ConversationId}, fetching new token", conversationId);
            request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, directLineSpeechUrl, fullUrl, content, false, cancellationToken);
        }
        
        var response = await _httpClient.SendAsync(request, cancellationToken);
        
        // If we get 403 and we didn't use a conversation-specific token, try with a fresh token from Copilot Studio
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && string.IsNullOrEmpty(directLineToken))
        {
            _logger.LogWarning("Got 403 Forbidden, refreshing token and retrying...");
            request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, directLineSpeechUrl, fullUrl, content, true, cancellationToken);
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        
        response.EnsureSuccessStatusCode();
        
        _logger.LogDebug("Sent activity to conversation {ConversationId} at {Timestamp}", conversationId, timestamp);
        return timestamp;
    }

    private async Task<(string? ResponseText, string? RawActivityJson)> WaitForBotResponseAsync(string directLineSpeechUrl, string directLineBaseUrl, string conversationId, DateTime userMessageTimestamp, string userInputText, string? directLineToken, CancellationToken cancellationToken)
    {
        // Use configurable timeout instead of hardcoded 30 seconds
        var timeoutSeconds = _settings.CopilotStudio.BotResponseTimeoutSeconds;
        var maxAttempts = timeoutSeconds; // 1 attempt per second
        var attempt = 0;
        string? watermark = null;
        HashSet<string> seenActivityIds = new HashSet<string>();
        
        _logger.LogInformation("Waiting for bot response with timeout of {TimeoutSeconds} seconds", timeoutSeconds);

        while (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var url = $"{directLineBaseUrl}/v3/directline/conversations/{conversationId}/activities";
                if (!string.IsNullOrEmpty(watermark))
                {
                    url += $"?watermark={watermark}";
                }

                HttpRequestMessage request;
                if (!string.IsNullOrEmpty(directLineToken))
                {
                    // Use the provided conversation-specific token
                    request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Authorization", $"Bearer {directLineToken}");
                }
                else
                {
                    // Fall back to fetching a token from Copilot Studio
                    _logger.LogWarning("No DirectLine token provided for polling conversation {ConversationId}, fetching new token", conversationId);
                    request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, directLineSpeechUrl, url, null, false, cancellationToken);
                }
                
                var response = await _httpClient.SendAsync(request, cancellationToken);
                
                // If we get 403 and we didn't use a conversation-specific token, try with a fresh token from Copilot Studio
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && string.IsNullOrEmpty(directLineToken))
                {
                    _logger.LogWarning("Got 403 Forbidden while polling activities, refreshing token and retrying...");
                    request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, directLineSpeechUrl, url, null, true, cancellationToken);
                    response = await _httpClient.SendAsync(request, cancellationToken);
                }
                
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var activitiesResponse = JsonSerializer.Deserialize<DirectLineActivitiesResponse>(content, _jsonOptions);

                if (activitiesResponse?.Activities != null)
                {
                    watermark = activitiesResponse.Watermark;
                    
                    // Log all activities for debugging - use Information level to ensure it's captured
                    _logger.LogInformation("WaitForBot: Received {Count} activities in conversation {ConversationId}, watermark={Watermark}", 
                        activitiesResponse.Activities.Length, conversationId, watermark);
                    
                    foreach (var activity in activitiesResponse.Activities)
                    {
                        var textPreview = activity.Text?.Length > 50 
                            ? activity.Text.Substring(0, 50) + "..." 
                            : activity.Text ?? "(no text)";
                        _logger.LogInformation("WaitForBot Activity: Type={Type}, From.Id={FromId}, Id={ActivityId}, Text={Text}, Timestamp={Timestamp}",
                            activity.Type, activity.From?.Id ?? "null", activity.Id ?? "null",
                            textPreview, activity.Timestamp);
                    }
                    
                    // Look for NEW bot responses that we haven't seen before
                    var botMessages = activitiesResponse.Activities
                        .Where(a => a.Type == "message" 
                            && !string.IsNullOrEmpty(a.Text)
                            && a.Text != userInputText  // NOT the exact same text we sent
                            && a.From?.Id != null 
                            && a.From.Id != "user"  // Not from user
                            && a.Timestamp > userMessageTimestamp  // Must be AFTER we sent our message (no buffer)
                            && !string.IsNullOrEmpty(a.Id)
                            && !seenActivityIds.Contains(a.Id))  // NEW activity we haven't processed
                        .ToList();

                    _logger.LogInformation("WaitForBot: Found {Count} NEW bot messages after filtering (attempt {Attempt})", 
                        botMessages.Count, attempt);

                    if (botMessages.Any())
                    {
                        // Return the FIRST new bot message (not last, to avoid skipping)
                        var newMessage = botMessages.First();
                        seenActivityIds.Add(newMessage.Id!);
                        var rawJson = JsonSerializer.Serialize(newMessage, _jsonOptions);
                        
                        _logger.LogWarning("[DIRECTLINE] Returning NEW bot response ActivityId={ActivityId}, Timestamp={Timestamp}, Text='{Text}'", 
                            newMessage.Id, newMessage.Timestamp, newMessage.Text?.Substring(0, Math.Min(100, newMessage.Text?.Length ?? 0)));
                        
                        return (newMessage.Text, rawJson);
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

        _logger.LogWarning("Timeout waiting for bot response in conversation {ConversationId} after {Attempts} attempts ({TimeoutSeconds} seconds)", 
            conversationId, attempt, timeoutSeconds);
        return (null, null);
    }

    public async Task<string> StartConversationAsync(string directLineSpeechUrl, CancellationToken cancellationToken = default)
    {
        var (conversationId, _) = await StartConversationInternalAsync(directLineSpeechUrl, cancellationToken);
        return conversationId;
    }

    /// <summary>
    /// Internal method that starts a conversation and returns both the conversation ID and the DirectLine token.
    /// The token is needed for multi-turn conversations as DirectLine binds conversations to tokens.
    /// </summary>
    private async Task<(string ConversationId, string DirectLineToken)> StartConversationInternalAsync(string directLineSpeechUrl, CancellationToken cancellationToken = default)
    {
        // Extract Direct Line base URL from the token endpoint
        var directLineBaseUrl = ExtractDirectLineBaseUrl(directLineSpeechUrl);
        
        return await _resilientService.ExecuteAsync(async (ct) =>
        {
            _logger.LogInformation("Starting conversation using endpoint {Endpoint}", directLineSpeechUrl);
            
            // Get a fresh token for this conversation
            var token = await GetValidTokenAsync(directLineSpeechUrl, false, ct);
            
            var fullUrl = $"{directLineBaseUrl}/v3/directline/conversations";
            var request = new HttpRequestMessage(HttpMethod.Post, fullUrl);
            request.Headers.Add("Authorization", $"Bearer {token}");
            
            var response = await _httpClient.SendAsync(request, ct);
            
            // If we get 403, try one more time with a fresh token
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Got 403 Forbidden while starting conversation, refreshing token and retrying...");
                token = await GetValidTokenAsync(directLineSpeechUrl, true, ct);
                request = new HttpRequestMessage(HttpMethod.Post, fullUrl);
                request.Headers.Add("Authorization", $"Bearer {token}");
                response = await _httpClient.SendAsync(request, ct);
            }
            
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(ct);
            var conversationResponse = JsonSerializer.Deserialize<DirectLineConversationResponse>(content, _jsonOptions);
            
            if (conversationResponse?.ConversationId == null)
            {
                throw new InvalidOperationException("Failed to create conversation - no conversation ID returned");
            }
            
            _logger.LogInformation("Started conversation {ConversationId} with token", 
                conversationResponse.ConversationId);
            
            return (conversationResponse.ConversationId, token);
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

    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_httpClient == null)
            {
                _logger.LogWarning("Copilot Studio HTTP client is not initialized");
                return Task.FromResult(false);
            }

            // Health check is basic since we don't have a specific endpoint to check
            // The actual health will be verified when a specific Direct Line URL is used
            _logger.LogDebug("Copilot Studio service is initialized and ready");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot Studio health check failed");
            return Task.FromResult(false);
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