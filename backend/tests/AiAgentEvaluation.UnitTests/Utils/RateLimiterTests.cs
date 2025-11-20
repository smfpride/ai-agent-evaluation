using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using AiAgentEvaluation.Utils;
using System.Diagnostics;

namespace AiAgentEvaluation.UnitTests.Utils;

public class RateLimiterTests
{
    private readonly Mock<ILogger> _mockLogger;

    public RateLimiterTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    [Fact]
    public void Constructor_InvalidMaxRequestsPerSecond_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new RateLimiter(_mockLogger.Object, 0, 10));
        Assert.Throws<ArgumentException>(() => new RateLimiter(_mockLogger.Object, -1, 10));
    }

    [Fact]
    public void Constructor_BurstCapacityLessThanMaxRequestsPerSecond_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new RateLimiter(_mockLogger.Object, 10, 5));
    }

    [Fact]
    public void Constructor_ValidParameters_CreatesRateLimiter()
    {
        // Arrange & Act
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 10, 20, 50);
        var stats = rateLimiter.GetStats();

        // Assert
        Assert.Equal(10, stats.MaxRequestsPerSecond);
        Assert.Equal(20, stats.BurstCapacity);
        Assert.Equal(20, stats.AvailableTokens); // Starts with full bucket
    }

    [Fact]
    public async Task AcquireAsync_FirstRequest_CompletesImmediately()
    {
        // Arrange
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 10, 20);
        var stopwatch = Stopwatch.StartNew();

        // Act
        using var ticket = await rateLimiter.AcquireAsync();
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 100); // Should complete quickly
        var stats = rateLimiter.GetStats();
        Assert.True(stats.AvailableTokens < 20); // Token consumed
    }

    [Fact]
    public async Task AcquireAsync_BurstRequests_AllowedUpToBurstCapacity()
    {
        // Arrange
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 10, 20);
        var tickets = new List<RateLimiterTicket>();

        // Act - Acquire 20 requests rapidly (burst capacity)
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++)
        {
            tickets.Add(await rateLimiter.AcquireAsync());
        }
        stopwatch.Stop();

        // Assert - All 20 should complete quickly (within burst capacity)
        Assert.True(stopwatch.ElapsedMilliseconds < 500);
        var stats = rateLimiter.GetStats();
        Assert.True(stats.AvailableTokens < 1); // Bucket should be nearly empty

        // Cleanup
        foreach (var ticket in tickets)
        {
            ticket.Dispose();
        }
    }

    [Fact]
    public async Task AcquireAsync_BeyondBurstCapacity_WaitsForTokenRefill()
    {
        // Arrange
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 10, 20); // 10 per second, burst 20
        var tickets = new List<RateLimiterTicket>();

        // Act - Exhaust burst capacity
        for (int i = 0; i < 20; i++)
        {
            tickets.Add(await rateLimiter.AcquireAsync());
        }

        // Request one more (should wait for refill)
        var stopwatch = Stopwatch.StartNew();
        using var delayedTicket = await rateLimiter.AcquireAsync();
        stopwatch.Stop();

        // Assert - Should wait at least 100ms (1/10th of a second for 1 token at 10 per second)
        Assert.True(stopwatch.ElapsedMilliseconds >= 80); // Allow some variance

        // Cleanup
        foreach (var ticket in tickets)
        {
            ticket.Dispose();
        }
    }

    [Fact]
    public async Task AcquireAsync_WithConcurrencyLimit_EnforcesMaxConcurrent()
    {
        // Arrange
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 100, 100, maxConcurrentRequests: 2);
        var ticket1 = await rateLimiter.AcquireAsync();
        var ticket2 = await rateLimiter.AcquireAsync();

        // Act - Try to acquire 3rd concurrent request
        var ticket3Task = rateLimiter.AcquireAsync();
        await Task.Delay(50); // Give it time to try

        // Assert - 3rd request should be waiting
        Assert.False(ticket3Task.IsCompleted);

        // Release one ticket
        ticket1.Dispose();
        await Task.Delay(50);

        // Now 3rd should complete
        Assert.True(ticket3Task.IsCompleted);
        
        // Cleanup
        ticket2.Dispose();
        (await ticket3Task).Dispose();
    }

    [Fact]
    public async Task AcquireAsync_TokenRefillOverTime_AllowsMoreRequests()
    {
        // Arrange
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 10, 10); // 10 per second
        var tickets = new List<RateLimiterTicket>();

        // Act - Exhaust bucket
        for (int i = 0; i < 10; i++)
        {
            tickets.Add(await rateLimiter.AcquireAsync());
        }

        var statsEmpty = rateLimiter.GetStats();
        Assert.True(statsEmpty.AvailableTokens < 1);

        // Wait for refill (1 second = 10 tokens)
        await Task.Delay(1100);

        // Acquire more requests (should succeed without long wait)
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
        {
            tickets.Add(await rateLimiter.AcquireAsync());
        }
        stopwatch.Stop();

        // Assert - Should complete quickly since tokens refilled
        Assert.True(stopwatch.ElapsedMilliseconds < 200);

        // Cleanup
        foreach (var ticket in tickets)
        {
            ticket.Dispose();
        }
    }

    [Fact]
    public async Task AcquireAsync_CancellationToken_CancelsWait()
    {
        // Arrange
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 1, 1); // Very slow rate
        var tickets = new List<RateLimiterTicket>();
        
        // Exhaust bucket
        tickets.Add(await rateLimiter.AcquireAsync());

        var cts = new CancellationTokenSource();
        cts.CancelAfter(100); // Cancel after 100ms

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            using var ticket = await rateLimiter.AcquireAsync(cts.Token);
        });

        // Cleanup
        foreach (var ticket in tickets)
        {
            ticket.Dispose();
        }
    }

    [Fact]
    public void GetStats_ReturnsAccurateStats()
    {
        // Arrange
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 10, 20, 50);

        // Act
        var stats = rateLimiter.GetStats();

        // Assert
        Assert.Equal(10, stats.MaxRequestsPerSecond);
        Assert.Equal(20, stats.BurstCapacity);
        Assert.Equal(20, stats.AvailableTokens);
        Assert.Equal(50, stats.AvailableConcurrentSlots);
    }

    [Fact]
    public async Task RateLimiterTicket_Dispose_ReleasesConcurrencySlot()
    {
        // Arrange
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 100, 100, maxConcurrentRequests: 1);
        
        // Act - Acquire and immediately dispose
        var ticket1 = await rateLimiter.AcquireAsync();
        var statsBefore = rateLimiter.GetStats();
        Assert.Equal(0, statsBefore.AvailableConcurrentSlots);

        ticket1.Dispose();
        await Task.Delay(10); // Give time for release

        // Assert - Slot should be available again
        var statsAfter = rateLimiter.GetStats();
        Assert.Equal(1, statsAfter.AvailableConcurrentSlots);
    }

    [Fact]
    public async Task RateLimiter_MultipleDispose_DoesNotThrow()
    {
        // Arrange
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 10, 20);
        var ticket = await rateLimiter.AcquireAsync();

        // Act & Assert - Multiple dispose should not throw
        ticket.Dispose();
        ticket.Dispose();
        ticket.Dispose();
    }

    [Fact]
    public async Task RateLimiter_HighConcurrency_MaintainsRateLimit()
    {
        // Arrange
        using var rateLimiter = new RateLimiter(_mockLogger.Object, 20, 40, maxConcurrentRequests: 100);
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task>();
        var completedCount = 0;
        var lockObj = new object();

        // Act - Simulate 100 concurrent requests
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var ticket = await rateLimiter.AcquireAsync();
                await Task.Delay(10); // Simulate work
                lock (lockObj)
                {
                    completedCount++;
                }
            }));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        Assert.Equal(100, completedCount);
        
        // At 20 req/s with burst of 40:
        // First 40 complete immediately (burst)
        // Remaining 60 at 20 req/s = 3 seconds
        // Total should be ~3+ seconds
        Assert.True(stopwatch.ElapsedMilliseconds >= 2500); // Allow some variance
        Assert.True(stopwatch.ElapsedMilliseconds < 5000);  // But not too slow
    }
}
