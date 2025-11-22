using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Utils;

/// <summary>
/// Resilient service wrapper combining retry policy and circuit breaker
/// </summary>
public class ResilientService
{
    private readonly RetryPolicy _retryPolicy;
    private readonly CircuitBreaker _circuitBreaker;

    public ResilientService(ILogger logger, IOptions<AppSettings> settings)
    {
        var appSettings = settings.Value;

        // Initialize retry policy with settings
        _retryPolicy = new RetryPolicy(
            logger,
            maxRetries: appSettings.Evaluation.MaxRetryAttempts,
            baseDelay: TimeSpan.FromSeconds(appSettings.Evaluation.RetryDelaySeconds),
            maxDelay: TimeSpan.FromSeconds(appSettings.Evaluation.MaxRetryAttempts * appSettings.Evaluation.RetryDelaySeconds),
            useJitter: true
        );

        // Initialize circuit breaker
        _circuitBreaker = new CircuitBreaker(
            logger,
            failureThreshold: 5,
            timeout: TimeSpan.FromMinutes(2),
            successThreshold: 3
        );
    }

    /// <summary>
    /// Execute an operation with both retry policy and circuit breaker protection
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        Func<Exception, bool>? customRetryLogic = null,
        CancellationToken cancellationToken = default)
    {
        return await _circuitBreaker.ExecuteAsync(async ct =>
        {
            return await _retryPolicy.ExecuteAsync(
                operation,
                customRetryLogic,
                operationName,
                ct
            );
        }, operationName, cancellationToken);
    }

    /// <summary>
    /// Execute an operation with both retry policy and circuit breaker protection (void return)
    /// </summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        Func<Exception, bool>? customRetryLogic = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async ct =>
        {
            await operation(ct);
            return true; // Dummy return value
        }, operationName, customRetryLogic, cancellationToken);
    }

    /// <summary>
    /// Get circuit breaker metrics for monitoring
    /// </summary>
    public CircuitBreakerMetrics GetCircuitBreakerMetrics()
    {
        return _circuitBreaker.GetMetrics();
    }
}

/// <summary>
/// Service-specific resilient wrapper
/// </summary>
public class ResilientServiceWrapper<T> where T : class
{
    private readonly T _service;
    private readonly ResilientService _resilientService;

    public ResilientServiceWrapper(T service, ILogger logger, IOptions<AppSettings> settings)
    {
        _service = service;
        _resilientService = new ResilientService(logger, settings);
    }

    public T Service => _service;
    
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<T, CancellationToken, Task<TResult>> operation,
        string operationName,
        Func<Exception, bool>? customRetryLogic = null,
        CancellationToken cancellationToken = default)
    {
        return await _resilientService.ExecuteAsync(
            ct => operation(_service, ct),
            $"{typeof(T).Name}.{operationName}",
            customRetryLogic,
            cancellationToken
        );
    }

    public async Task ExecuteAsync(
        Func<T, CancellationToken, Task> operation,
        string operationName,
        Func<Exception, bool>? customRetryLogic = null,
        CancellationToken cancellationToken = default)
    {
        await _resilientService.ExecuteAsync(
            ct => operation(_service, ct),
            $"{typeof(T).Name}.{operationName}",
            customRetryLogic,
            cancellationToken
        );
    }

    public CircuitBreakerMetrics GetMetrics() => _resilientService.GetCircuitBreakerMetrics();
}

/// <summary>
/// Extension methods for easy resilient service creation
/// </summary>
public static class ResilientServiceExtensions
{
    public static ResilientServiceWrapper<T> WithResilience<T>(
        this T service,
        ILogger logger,
        IOptions<AppSettings> settings) where T : class
    {
        return new ResilientServiceWrapper<T>(service, logger, settings);
    }
}