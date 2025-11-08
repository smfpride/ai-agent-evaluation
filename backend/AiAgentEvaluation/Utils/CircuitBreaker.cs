using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Utils;

/// <summary>
/// Circuit breaker state
/// </summary>
public enum CircuitBreakerState
{
    Closed,     // Normal operation
    Open,       // Failing, reject requests
    HalfOpen    // Testing if service is back
}

/// <summary>
/// Circuit breaker pattern implementation
/// </summary>
public class CircuitBreaker
{
    private readonly ILogger _logger;
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly int _successThreshold;
    private readonly object _lock = new();
    
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private int _successCount = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;

    public CircuitBreaker(ILogger logger, int failureThreshold = 5, TimeSpan? timeout = null, int successThreshold = 3)
    {
        _logger = logger;
        _failureThreshold = failureThreshold;
        _timeout = timeout ?? TimeSpan.FromMinutes(1);
        _successThreshold = successThreshold;
    }

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Execute an operation with circuit breaker protection
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        // Check if circuit breaker allows the call
        if (!CanExecute())
        {
            throw new CircuitBreakerOpenException($"Circuit breaker is open for {operationName ?? "operation"}");
        }

        try
        {
            var result = await operation(cancellationToken);
            OnSuccess(operationName);
            return result;
        }
        catch (Exception ex)
        {
            OnFailure(ex, operationName);
            throw;
        }
    }

    /// <summary>
    /// Execute an operation with circuit breaker protection (void return)
    /// </summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async ct =>
        {
            await operation(ct);
            return true; // Dummy return value
        }, operationName, cancellationToken);
    }

    private bool CanExecute()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    return true;

                case CircuitBreakerState.Open:
                    // Check if timeout has elapsed
                    if (DateTime.UtcNow >= _lastFailureTime.Add(_timeout))
                    {
                        _state = CircuitBreakerState.HalfOpen;
                        _successCount = 0;
                        _logger.LogInformation("Circuit breaker transitioning from Open to HalfOpen after {Timeout} timeout", _timeout);
                        return true;
                    }
                    return false;

                case CircuitBreakerState.HalfOpen:
                    return true;

                default:
                    return false;
            }
        }
    }

    private void OnSuccess(string? operationName)
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    _failureCount = 0;
                    break;

                case CircuitBreakerState.HalfOpen:
                    _successCount++;
                    if (_successCount >= _successThreshold)
                    {
                        _state = CircuitBreakerState.Closed;
                        _failureCount = 0;
                        _successCount = 0;
                        _logger.LogInformation("Circuit breaker for {OperationName} closed after {SuccessCount} successful attempts", 
                            operationName ?? "operation", _successCount);
                    }
                    break;
            }
        }
    }

    private void OnFailure(Exception ex, string? operationName)
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;
            _successCount = 0;

            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    if (_failureCount >= _failureThreshold)
                    {
                        _state = CircuitBreakerState.Open;
                        _logger.LogWarning("Circuit breaker for {OperationName} opened after {FailureCount} failures. Last error: {Error}", 
                            operationName ?? "operation", _failureCount, ex.Message);
                    }
                    break;

                case CircuitBreakerState.HalfOpen:
                    _state = CircuitBreakerState.Open;
                    _logger.LogWarning("Circuit breaker for {OperationName} returned to Open state after failure in HalfOpen. Error: {Error}", 
                        operationName ?? "operation", ex.Message);
                    break;
            }
        }
    }

    /// <summary>
    /// Get current circuit breaker metrics
    /// </summary>
    public CircuitBreakerMetrics GetMetrics()
    {
        lock (_lock)
        {
            return new CircuitBreakerMetrics
            {
                State = _state,
                FailureCount = _failureCount,
                SuccessCount = _successCount,
                LastFailureTime = _lastFailureTime,
                TimeUntilRetry = _state == CircuitBreakerState.Open 
                    ? _lastFailureTime.Add(_timeout).Subtract(DateTime.UtcNow)
                    : TimeSpan.Zero
            };
        }
    }
}

/// <summary>
/// Circuit breaker metrics
/// </summary>
public class CircuitBreakerMetrics
{
    public CircuitBreakerState State { get; set; }
    public int FailureCount { get; set; }
    public int SuccessCount { get; set; }
    public DateTime LastFailureTime { get; set; }
    public TimeSpan TimeUntilRetry { get; set; }
}

/// <summary>
/// Exception thrown when circuit breaker is open
/// </summary>
public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
    public CircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException) { }
}