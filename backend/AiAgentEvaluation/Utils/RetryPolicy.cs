using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

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
                    var delay = CalculateDelay(attempt);
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

    private TimeSpan CalculateDelay(int attempt)
    {
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
            System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> dataverseFault => IsRetriableDataverseFault(dataverseFault),
            Azure.Messaging.ServiceBus.ServiceBusException sbEx => IsRetriableServiceBusException(sbEx),
            System.Net.WebException webEx => IsRetriableWebException(webEx),
            _ => false
        };
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