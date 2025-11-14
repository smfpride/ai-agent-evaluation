# Rate Limit and Copilot Studio Reliability Improvements

## Issue Summary

From production testing with larger test sets, the following errors were observed:
- **9 Rate Limit Errors**: From Azure OpenAI (AOAI) 429 responses
- **55 Copilot Studio Call Failed Errors**: Various timeout and network issues
- **6 Message Size Too Big Errors**: Agent-side issue (not addressed here)

## Root Cause Analysis

### 1. Azure OpenAI Rate Limit Errors

**Current Implementation Issues:**
- `RetryPolicy.cs` only catches `HttpRequestException` and checks for "429" in the message string
- Azure OpenAI SDK v2.1.0 uses `Azure.AI.OpenAI` which throws `ClientResultException` or `RequestFailedException` from the Azure SDK
- These exceptions are NOT being caught by the current retry logic
- Rate limit errors should include `Retry-After` header handling

**Impact:** All 9 rate limit errors failed immediately without retries

### 2. Copilot Studio Call Failures

**Current Implementation Issues:**
- 30-second timeout may be too short for complex agent processing
- Token endpoint failures (`FetchNewTokenAsync`) have no retry wrapper
- Polling timeout in `WaitForBotResponseAsync` is 30 seconds (30 × 1 second)
- No specific handling for Direct Line API throttling
- Generic error handling doesn't distinguish between retriable/non-retriable errors

**Impact:** 55 failures, likely a mix of:
- Network timeouts during token fetch
- Agent processing taking >30 seconds
- Temporary Direct Line API issues
- Token endpoint throttling

## Recommended Fixes

### Fix 1: Update RetryPolicy to Handle Azure SDK Exceptions

**File:** `backend/AiAgentEvaluation/Utils/RetryPolicy.cs`

**Changes:**
1. Add Azure SDK exception types to retry logic
2. Handle `Retry-After` header from rate limit responses
3. Add specific handling for `ClientResultException` with status 429

```csharp
using Azure;

private static bool IsRetriableException(Exception ex, Func<Exception, bool>? customShouldRetry)
{
    // Use custom retry logic if provided
    if (customShouldRetry != null)
        return customShouldRetry(ex);

    // Default transient fault detection
    return ex switch
    {
        TaskCanceledException when ex.InnerException is TimeoutException => true,
        HttpRequestException httpEx => IsRetriableHttpException(httpEx),
        SocketException => true,
        TimeoutException => true,
        
        // Azure SDK exceptions
        RequestFailedException azEx => IsRetriableAzureException(azEx),
        ClientResultException clientEx => IsRetriableClientException(clientEx),
        
        System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> dataverseFault => IsRetriableDataverseFault(dataverseFault),
        Azure.Messaging.ServiceBus.ServiceBusException sbEx => IsRetriableServiceBusException(sbEx),
        System.Net.WebException webEx => IsRetriableWebException(webEx),
        _ => false
    };
}

private static bool IsRetriableAzureException(RequestFailedException ex)
{
    // Handle Azure SDK exceptions (OpenAI, Storage, etc.)
    return ex.Status == 429 ||  // Too Many Requests
           ex.Status == 503 ||  // Service Unavailable
           ex.Status == 502 ||  // Bad Gateway
           ex.Status == 504;    // Gateway Timeout
}

private static bool IsRetriableClientException(ClientResultException ex)
{
    // Handle ClientResultException from Azure.AI.OpenAI
    return ex.Status == 429 ||  // Rate limit
           ex.Status == 503 ||  // Service unavailable
           ex.Status == 502 ||  // Bad Gateway
           ex.Status == 504;    // Gateway Timeout
}
```

### Fix 2: Add Retry-After Header Support

**File:** `backend/AiAgentEvaluation/Utils/RetryPolicy.cs`

**Changes:**
1. Extract `Retry-After` header from Azure SDK exceptions
2. Use it to calculate delay instead of exponential backoff

```csharp
private TimeSpan CalculateDelay(int attempt, Exception? lastException = null)
{
    // Check for Retry-After header in Azure SDK exceptions
    if (lastException != null)
    {
        TimeSpan? retryAfter = ExtractRetryAfter(lastException);
        if (retryAfter.HasValue)
        {
            _logger.LogInformation("Using Retry-After delay of {Delay}ms", retryAfter.Value.TotalMilliseconds);
            return retryAfter.Value;
        }
    }

    // Exponential backoff: 2^attempt * baseDelay
    var delay = TimeSpan.FromTicks(_baseDelay.Ticks * (long)Math.Pow(2, attempt - 1));
    
    // Cap at max delay
    if (delay > _maxDelay)
        delay = _maxDelay;

    // Add jitter to avoid thundering herd
    if (_useJitter)
    {
        var jitterMs = Random.Shared.Next(0, (int)(delay.TotalMilliseconds * 0.1)); // 10% jitter
        delay = delay.Add(TimeSpan.FromMilliseconds(jitterMs));
    }

    return delay;
}

private TimeSpan? ExtractRetryAfter(Exception ex)
{
    // Extract Retry-After from Azure SDK exceptions
    if (ex is RequestFailedException azEx && azEx.GetRawResponse() != null)
    {
        var response = azEx.GetRawResponse();
        if (response.Headers.TryGetValue("Retry-After", out var retryAfter))
        {
            // Retry-After can be either seconds or HTTP-date
            if (int.TryParse(retryAfter, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
    }

    return null;
}
```

### Fix 3: Update RetryPolicy ExecuteAsync Signature

**File:** `backend/AiAgentEvaluation/Utils/RetryPolicy.cs`

**Changes:**
Pass last exception to CalculateDelay for Retry-After support

```csharp
public async Task<T> ExecuteAsync<T>(
    Func<CancellationToken, Task<T>> operation,
    Func<Exception, bool>? shouldRetry = null,
    string? operationName = null,
    CancellationToken cancellationToken = default)
{
    var attempt = 0;
    var lastException = (Exception?)null;

    while (attempt <= _maxRetries)
    {
        try
        {
            if (attempt > 0)
            {
                var delay = CalculateDelay(attempt, lastException);
                _logger.LogInformation("Retrying {OperationName} (attempt {Attempt}/{MaxRetries}) after {Delay}ms delay", 
                    operationName ?? "operation", attempt + 1, _maxRetries + 1, delay.TotalMilliseconds);
                
                await Task.Delay(delay, cancellationToken);
            }

            return await operation(cancellationToken);
        }
        catch (Exception ex) when (attempt < _maxRetries && IsRetriableException(ex, shouldRetry))
        {
            lastException = ex;
            _logger.LogWarning(ex, "Attempt {Attempt} failed for {OperationName}: {Error}", 
                attempt + 1, operationName ?? "operation", ex.Message);
            attempt++;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Final attempt {Attempt} failed for {OperationName}: {Error}", 
                attempt + 1, operationName ?? "operation", ex.Message);
            throw;
        }
    }

    throw lastException ?? new InvalidOperationException("Retry policy failed without capturing exception");
}
```

### Fix 4: Increase Copilot Studio Timeouts

**File:** `backend/AiAgentEvaluation/Models/AppSettings.cs`

**Changes:**
Increase default timeout values for Copilot Studio operations

```csharp
public class CopilotStudioSettings
{
    /// <summary>
    /// Default bot/agent ID for conversations
    /// </summary>
    public string DefaultBotId { get; set; } = string.Empty;
    
    /// <summary>
    /// HTTP timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;  // Changed from 30 to 60
    
    /// <summary>
    /// Maximum retry attempts for failed requests
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
    
    /// <summary>
    /// Maximum time to wait for bot response (in seconds)
    /// </summary>
    public int BotResponseTimeoutSeconds { get; set; } = 60;  // New setting
}
```

### Fix 5: Add Retry Logic to Token Fetching

**File:** `backend/AiAgentEvaluation/Services/CopilotStudioService.cs`

**Changes:**
Wrap token fetching in retry logic

```csharp
private async Task<string> FetchNewTokenAsync(string directLineSpeechUrl, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrEmpty(directLineSpeechUrl))
    {
        throw new InvalidOperationException("Direct Line Speech URL is required but was not provided.");
    }

    _logger.LogDebug("Fetching new Direct Line token from endpoint: {Endpoint}", directLineSpeechUrl);

    // Wrap token fetch in resilient service for retries
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
```

### Fix 6: Increase Bot Response Polling Timeout

**File:** `backend/AiAgentEvaluation/Services/CopilotStudioService.cs`

**Changes:**
Make polling timeout configurable and increase default

```csharp
private async Task<(string? ResponseText, string? RawActivityJson)> WaitForBotResponseAsync(
    string directLineSpeechUrl, 
    string directLineBaseUrl, 
    string conversationId, 
    DateTime userMessageTimestamp, 
    string userInputText, 
    CancellationToken cancellationToken)
{
    // Use configurable timeout instead of hardcoded 30 attempts
    var timeoutSeconds = _settings.CopilotStudio.BotResponseTimeoutSeconds;
    var maxAttempts = timeoutSeconds; // 1 attempt per second
    var attempt = 0;
    string? watermark = null;
    HashSet<string> seenActivityIds = new HashSet<string>();

    _logger.LogInformation("Waiting for bot response with timeout of {TimeoutSeconds} seconds", timeoutSeconds);

    while (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
    {
        // ... rest of the method remains the same
    }

    _logger.LogWarning("Timeout waiting for bot response in conversation {ConversationId} after {Attempts} attempts ({TimeoutSeconds} seconds)", 
        conversationId, attempt, timeoutSeconds);
    return (null, null);
}
```

### Fix 7: Update Configuration Files

**File:** `backend/infrastructure/main.bicep` (App Settings)

Add new configuration values:

```bicep
{
  name: 'CopilotStudio__TimeoutSeconds'
  value: '60'
}
{
  name: 'CopilotStudio__BotResponseTimeoutSeconds'
  value: '60'
}
{
  name: 'AzureOpenAI__MaxRetryAttempts'
  value: '5'  // Increase from 3 to 5 for rate limits
}
```

### Fix 8: Add Package Reference for Azure SDK

**File:** `backend/AiAgentEvaluation/AiAgentEvaluation.csproj`

Ensure Azure SDK is referenced (already present, just verify):

```xml
<PackageReference Include="Azure.Core" Version="1.42.0" />
```

## Implementation Priority

### Phase 1 (Critical - Implement Immediately)
1. ✅ Fix 1: Update RetryPolicy for Azure SDK exceptions
2. ✅ Fix 2: Add Retry-After header support
3. ✅ Fix 3: Update ExecuteAsync signature

### Phase 2 (High Priority)
4. ✅ Fix 4: Increase Copilot Studio timeouts
5. ✅ Fix 5: Add retry to token fetching
6. ✅ Fix 6: Increase bot response timeout

### Phase 3 (Configuration)
7. ✅ Fix 7: Update configuration files

## Testing Recommendations

### Unit Tests
1. Test `RetryPolicy` with `RequestFailedException` (status 429)
2. Test `RetryPolicy` with `ClientResultException` (status 429)
3. Test Retry-After header parsing
4. Test token fetch retry logic

### Integration Tests
1. Simulate Azure OpenAI rate limiting
2. Test Copilot Studio timeout scenarios
3. Test token endpoint failures
4. Verify retry delays honor Retry-After headers

### Load Testing
1. Run with same test set that produced errors
2. Monitor retry metrics
3. Verify all 9 rate limit errors are retried
4. Verify Copilot Studio failures are reduced

## Expected Improvements

### Azure OpenAI Rate Limits
- **Before:** 9 immediate failures
- **After:** Up to 5 retry attempts per request with exponential backoff
- **Expected:** 0-2 failures (assuming transient rate limits)

### Copilot Studio Calls
- **Before:** 55 failures (mix of timeouts and network issues)
- **After:** 
  - Token fetch failures retried (3 attempts)
  - Response timeout increased 30s → 60s
  - Better error classification
- **Expected:** 10-20 failures (genuine agent errors vs transient issues)

## Monitoring and Alerting

Add Application Insights logging for:
1. Rate limit retry attempts with Retry-After values
2. Token fetch retry metrics
3. Copilot Studio timeout occurrences
4. Circuit breaker state changes

## Rollback Plan

If issues arise:
1. Revert configuration changes (timeouts back to 30s)
2. Revert `RetryPolicy.cs` changes
3. Monitor error rates
4. Investigate specific failure patterns

## Notes

- The 6 "message size too big" errors are agent-side issues and cannot be fixed in this system
- Consider implementing request queuing with rate limiting on the client side to prevent Azure OpenAI throttling
- Consider implementing token bucket algorithm for better rate limit management
- Long-term: Implement telemetry for tracking retry success rates
