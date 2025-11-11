using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using Azure;
using System.ClientModel;

namespace AiAgentEvaluation.Utils;

/// <summary>
/// Retry policy with exponential backoff and jitter
/// </summary>
public class RetryPolicy
{
    private readonly ILogger _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly bool _useJitter;

    public RetryPolicy(ILogger logger, int maxRetries = 3, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null, bool useJitter = true)
    {
        _logger = logger;
        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromMinutes(5);
        _useJitter = useJitter;
    }

    /// <summary>
    /// Execute an async operation with retry policy
    /// </summary>
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

        // This should never be reached, but just in case
        throw lastException ?? new InvalidOperationException("Retry policy failed without capturing exception");
    }

    /// <summary>
    /// Execute an async operation with retry policy (void return)
    /// </summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        Func<Exception, bool>? shouldRetry = null,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async ct => 
        {
            await operation(ct);
            return true; // Dummy return value
        }, shouldRetry, operationName, cancellationToken);
    }

    private TimeSpan CalculateDelay(int attempt, Exception? lastException = null)
    {
        // Check for Retry-After header in Azure SDK exceptions
        if (lastException != null)
        {
            TimeSpan? retryAfter = ExtractRetryAfter(lastException);
            if (retryAfter.HasValue)
            {
                _logger.LogInformation("Using Retry-After delay of {Delay}ms from exception", retryAfter.Value.TotalMilliseconds);
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
        // Extract Retry-After from Azure SDK RequestFailedException
        if (ex is RequestFailedException azEx)
        {
            try
            {
                var response = azEx.GetRawResponse();
                if (response?.Headers != null && response.Headers.TryGetValue("Retry-After", out var retryAfter))
                {
                    // Retry-After can be either seconds (int) or HTTP-date
                    if (int.TryParse(retryAfter, out var seconds))
                    {
                        _logger.LogInformation("Extracted Retry-After header: {Seconds} seconds", seconds);
                        return TimeSpan.FromSeconds(seconds);
                    }
                    // If it's an HTTP-date, parse and calculate diff
                    else if (DateTimeOffset.TryParse(retryAfter, out var retryDate))
                    {
                        var delay = retryDate - DateTimeOffset.UtcNow;
                        if (delay.TotalSeconds > 0)
                        {
                            _logger.LogInformation("Extracted Retry-After date: {RetryDate}, delay: {Seconds} seconds", 
                                retryDate, delay.TotalSeconds);
                            return delay;
                        }
                    }
                }
            }
            catch (Exception extractEx)
            {
                _logger.LogDebug(extractEx, "Failed to extract Retry-After header, using exponential backoff");
            }
        }
        
        // Extract Retry-After from ClientResultException (Azure OpenAI SDK v2.1.0+)
        // The message format is: "HTTP 429 (429)\r\n\r\nRate limit is exceeded. Try again in X seconds."
        if (ex is ClientResultException clientEx)
        {
            try
            {
                var message = clientEx.Message;
                var match = System.Text.RegularExpressions.Regex.Match(message, @"Try again in (\d+) seconds?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds))
                {
                    _logger.LogInformation("Extracted Retry-After from ClientResultException message: {Seconds} seconds", seconds);
                    return TimeSpan.FromSeconds(seconds);
                }
            }
            catch (Exception extractEx)
            {
                _logger.LogDebug(extractEx, "Failed to extract Retry-After from ClientResultException message, using exponential backoff");
            }
        }

        return null;
    }

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
            
            // Azure SDK exceptions (for Azure OpenAI, Storage, etc.)
            RequestFailedException azEx => IsRetriableAzureException(azEx),
            
            // System.ClientModel exceptions (from Azure OpenAI SDK v2.1.0+)
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
        // Status code 429 = Too Many Requests (rate limit)
        // Status code 503 = Service Unavailable
        // Status code 502 = Bad Gateway
        // Status code 504 = Gateway Timeout
        return ex.Status == 429 ||
               ex.Status == 503 ||
               ex.Status == 502 ||
               ex.Status == 504;
    }

    private static bool IsRetriableClientException(ClientResultException ex)
    {
        // Handle ClientResultException from Azure.AI.OpenAI SDK v2.1.0+
        // This is the new exception type used by the Azure OpenAI SDK
        return ex.Status == 429 ||  // Rate limit
               ex.Status == 503 ||  // Service unavailable
               ex.Status == 502 ||  // Bad Gateway
               ex.Status == 504;    // Gateway Timeout
    }

    private static bool IsRetriableHttpException(HttpRequestException ex)
    {
        // Check for specific HTTP status codes that should be retried
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("timeout") ||
               message.Contains("503") || // Service Unavailable
               message.Contains("502") || // Bad Gateway
               message.Contains("504") || // Gateway Timeout
               message.Contains("429") || // Too Many Requests
               message.Contains("connection");
    }

    private static bool IsRetriableDataverseFault(System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> fault)
    {
        if (fault.Detail?.ErrorCode != null)
        {
            // Common transient Dataverse error codes
            var errorCode = fault.Detail.ErrorCode;
            return errorCode == -2147220970 || // Throttling
                   errorCode == -2147220969 || // Too many requests
                   errorCode == -2147204784 || // SQL timeout
                   errorCode == -2147220891;   // Service unavailable
        }
        
        return false;
    }

    private static bool IsRetriableServiceBusException(Azure.Messaging.ServiceBus.ServiceBusException ex)
    {
        return ex.Reason == Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceTimeout ||
               ex.Reason == Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceCommunicationProblem ||
               ex.Reason == Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceBusy;
    }

    private static bool IsRetriableWebException(WebException ex)
    {
        return ex.Status == WebExceptionStatus.Timeout ||
               ex.Status == WebExceptionStatus.ConnectFailure ||
               ex.Status == WebExceptionStatus.ReceiveFailure ||
               ex.Status == WebExceptionStatus.SendFailure ||
               ex.Status == WebExceptionStatus.KeepAliveFailure;
    }
}