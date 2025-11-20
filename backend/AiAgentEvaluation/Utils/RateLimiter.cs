using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Utils;

/// <summary>
/// Token bucket rate limiter with burst capacity and concurrency control
/// Prevents overwhelming external services (like Copilot Studio) with too many concurrent requests
/// </summary>
public class RateLimiter : IDisposable
{
    private readonly ILogger _logger;
    private readonly int _maxRequestsPerSecond;
    private readonly int _burstCapacity;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly SemaphoreSlim _bucketSemaphore = new(1, 1);
    private double _availableTokens;
    private DateTime _lastRefillTime;
    private bool _disposed;

    /// <summary>
    /// Creates a new rate limiter with token bucket algorithm
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="maxRequestsPerSecond">Maximum requests per second (sustainable rate)</param>
    /// <param name="burstCapacity">Maximum burst capacity (allows short bursts above sustained rate)</param>
    /// <param name="maxConcurrentRequests">Maximum concurrent requests allowed (0 = unlimited)</param>
    public RateLimiter(
        ILogger logger,
        int maxRequestsPerSecond,
        int burstCapacity,
        int maxConcurrentRequests = 0)
    {
        if (maxRequestsPerSecond <= 0)
            throw new ArgumentException("Max requests per second must be greater than 0", nameof(maxRequestsPerSecond));
        
        if (burstCapacity < maxRequestsPerSecond)
            throw new ArgumentException("Burst capacity must be >= max requests per second", nameof(burstCapacity));

        _logger = logger;
        _maxRequestsPerSecond = maxRequestsPerSecond;
        _burstCapacity = burstCapacity;
        _availableTokens = burstCapacity; // Start with full bucket
        _lastRefillTime = DateTime.UtcNow;

        // Create concurrency semaphore if max concurrent requests is specified
        _concurrencySemaphore = maxConcurrentRequests > 0
            ? new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests)
            : new SemaphoreSlim(int.MaxValue, int.MaxValue); // Effectively unlimited

        _logger.LogInformation(
            "RateLimiter initialized: MaxRequestsPerSecond={MaxRequestsPerSecond}, BurstCapacity={BurstCapacity}, MaxConcurrentRequests={MaxConcurrentRequests}",
            _maxRequestsPerSecond,
            _burstCapacity,
            maxConcurrentRequests > 0 ? maxConcurrentRequests : "unlimited");
    }

    /// <summary>
    /// Acquires permission to make a request, waiting if necessary
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when permission is granted</returns>
    public async Task<RateLimiterTicket> AcquireAsync(CancellationToken cancellationToken = default)
    {
        // First, acquire concurrency slot (wait if at max concurrent requests)
        await _concurrencySemaphore.WaitAsync(cancellationToken);

        try
        {
            // Then, acquire token from bucket (wait if bucket is empty)
            await WaitForTokenAsync(cancellationToken);

            _logger.LogDebug("Rate limiter: Request acquired, available tokens: {AvailableTokens:F2}", _availableTokens);

            // Return ticket that will release concurrency slot when disposed
            return new RateLimiterTicket(_concurrencySemaphore);
        }
        catch
        {
            // If we fail to acquire token, release the concurrency slot
            _concurrencySemaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// Waits for a token to become available in the bucket
    /// </summary>
    private async Task WaitForTokenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _bucketSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Refill tokens based on time elapsed
                RefillBucket();

                // If token is available, consume it and return
                if (_availableTokens >= 1.0)
                {
                    _availableTokens -= 1.0;
                    return;
                }

                // Calculate how long to wait for next token
                var tokensNeeded = 1.0 - _availableTokens;
                var secondsToWait = tokensNeeded / _maxRequestsPerSecond;
                var delayMs = (int)Math.Ceiling(secondsToWait * 1000);

                _logger.LogDebug(
                    "Rate limiter: No tokens available, waiting {DelayMs}ms (available: {AvailableTokens:F2})",
                    delayMs,
                    _availableTokens);

                // Release lock before waiting
                _bucketSemaphore.Release();

                // Wait for tokens to refill
                await Task.Delay(delayMs, cancellationToken);
            }
            catch
            {
                _bucketSemaphore.Release();
                throw;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Refills the token bucket based on elapsed time
    /// </summary>
    private void RefillBucket()
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = (now - _lastRefillTime).TotalSeconds;

        if (elapsedSeconds > 0)
        {
            // Add tokens based on elapsed time
            var tokensToAdd = elapsedSeconds * _maxRequestsPerSecond;
            _availableTokens = Math.Min(_burstCapacity, _availableTokens + tokensToAdd);
            _lastRefillTime = now;
        }
    }

    /// <summary>
    /// Gets current rate limiter statistics
    /// </summary>
    public RateLimiterStats GetStats()
    {
        RefillBucket();
        return new RateLimiterStats
        {
            AvailableTokens = _availableTokens,
            MaxRequestsPerSecond = _maxRequestsPerSecond,
            BurstCapacity = _burstCapacity,
            AvailableConcurrentSlots = _concurrencySemaphore.CurrentCount
        };
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
            _concurrencySemaphore?.Dispose();
            _bucketSemaphore?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Ticket returned by rate limiter that releases concurrency slot when disposed
/// </summary>
public class RateLimiterTicket : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private bool _disposed;

    internal RateLimiterTicket(SemaphoreSlim semaphore)
    {
        _semaphore = semaphore;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _semaphore.Release();
            _disposed = true;
        }
    }
}

/// <summary>
/// Current rate limiter statistics
/// </summary>
public class RateLimiterStats
{
    public double AvailableTokens { get; init; }
    public int MaxRequestsPerSecond { get; init; }
    public int BurstCapacity { get; init; }
    public int AvailableConcurrentSlots { get; init; }
}
