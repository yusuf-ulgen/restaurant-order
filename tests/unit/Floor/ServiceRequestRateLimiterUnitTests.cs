using RestaurantOrder.Application.Floor;
using RestaurantOrder.Infrastructure.Floor;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class ServiceRequestRateLimiterUnitTests
{
    [Fact]
    public async Task EnsureNotRateLimited_FirstRequest_Succeeds()
    {
        var limiter = new InMemoryServiceRequestRateLimiter(cooldownPeriod: TimeSpan.FromSeconds(5));
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await limiter.EnsureNotRateLimitedAsync(tenantId, sessionId);
    }

    [Fact]
    public async Task EnsureNotRateLimited_WithinCooldown_ThrowsRateLimitException()
    {
        var limiter = new InMemoryServiceRequestRateLimiter(cooldownPeriod: TimeSpan.FromSeconds(15));
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await limiter.EnsureNotRateLimitedAsync(tenantId, sessionId);

        var ex = await Assert.ThrowsAsync<ServiceRequestRateLimitException>(() =>
            limiter.EnsureNotRateLimitedAsync(tenantId, sessionId));

        Assert.True(ex.RetryAfterSeconds > 0);
        Assert.True(ex.RetryAfterSeconds <= 15);
        Assert.Contains("wait", ex.Message);
    }

    [Fact]
    public async Task EnsureNotRateLimited_ExceedingSlidingWindowLimit_ThrowsRateLimitException()
    {
        // 0s cooldown, max 3 requests per 10s window
        var limiter = new InMemoryServiceRequestRateLimiter(
            cooldownPeriod: TimeSpan.Zero,
            windowPeriod: TimeSpan.FromSeconds(10),
            maxRequestsPerWindow: 3);

        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await limiter.EnsureNotRateLimitedAsync(tenantId, sessionId);
        await limiter.EnsureNotRateLimitedAsync(tenantId, sessionId);
        await limiter.EnsureNotRateLimitedAsync(tenantId, sessionId);

        var ex = await Assert.ThrowsAsync<ServiceRequestRateLimitException>(() =>
            limiter.EnsureNotRateLimitedAsync(tenantId, sessionId));

        Assert.True(ex.RetryAfterSeconds > 0);
        Assert.Contains("rate limit exceeded", ex.Message);
    }

    [Fact]
    public async Task EnsureNotRateLimited_DifferentSessions_HaveIsolatedBuckets()
    {
        var limiter = new InMemoryServiceRequestRateLimiter(cooldownPeriod: TimeSpan.FromSeconds(15));
        var tenantId = Guid.NewGuid();
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        await limiter.EnsureNotRateLimitedAsync(tenantId, session1);
        // Session 2 should not be affected by session 1
        await limiter.EnsureNotRateLimitedAsync(tenantId, session2);
    }

    [Fact]
    public async Task Reset_ClearsRateLimitState()
    {
        var limiter = new InMemoryServiceRequestRateLimiter(cooldownPeriod: TimeSpan.FromSeconds(15));
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await limiter.EnsureNotRateLimitedAsync(tenantId, sessionId);
        limiter.Reset(tenantId, sessionId);

        // After reset, request should succeed immediately
        await limiter.EnsureNotRateLimitedAsync(tenantId, sessionId);
    }
}
