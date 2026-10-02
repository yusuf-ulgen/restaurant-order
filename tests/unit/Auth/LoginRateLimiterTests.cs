using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class LoginRateLimiterTests
{
    [Fact]
    public async Task RateLimiter_UnderLimit_DoesNotLimit()
    {
        var limiter = new LoginRateLimiter(maxFailedAttempts: 3);

        await limiter.RecordFailedAttemptAsync("127.0.0.1", "user@test.com", "tenant-a");
        await limiter.RecordFailedAttemptAsync("127.0.0.1", "user@test.com", "tenant-a");

        var isLimited = await limiter.IsRateLimitedAsync("127.0.0.1", "user@test.com", "tenant-a");
        Assert.False(isLimited);
    }

    [Fact]
    public async Task RateLimiter_ExceedingLimit_ReturnsTrue()
    {
        var limiter = new LoginRateLimiter(maxFailedAttempts: 3);

        for (int i = 0; i < 3; i++)
        {
            await limiter.RecordFailedAttemptAsync("127.0.0.1", "user@test.com", "tenant-a");
        }

        var isLimited = await limiter.IsRateLimitedAsync("127.0.0.1", "user@test.com", "tenant-a");
        Assert.True(isLimited);
    }

    [Fact]
    public async Task RateLimiter_DifferentBuckets_AreIndependent()
    {
        var limiter = new LoginRateLimiter(maxFailedAttempts: 2);

        await limiter.RecordFailedAttemptAsync("127.0.0.1", "user1@test.com", "tenant-a");
        await limiter.RecordFailedAttemptAsync("127.0.0.1", "user1@test.com", "tenant-a");

        // user1 should be limited
        Assert.True(await limiter.IsRateLimitedAsync("127.0.0.1", "user1@test.com", "tenant-a"));

        // user2 on same IP should NOT be limited
        Assert.False(await limiter.IsRateLimitedAsync("127.0.0.1", "user2@test.com", "tenant-a"));

        // user1 on different IP should NOT be limited
        Assert.False(await limiter.IsRateLimitedAsync("192.168.1.1", "user1@test.com", "tenant-a"));
    }

    [Fact]
    public async Task ResetAttempts_ClearsRateLimit()
    {
        var limiter = new LoginRateLimiter(maxFailedAttempts: 2);

        await limiter.RecordFailedAttemptAsync("127.0.0.1", "user@test.com", "tenant-a");
        await limiter.RecordFailedAttemptAsync("127.0.0.1", "user@test.com", "tenant-a");
        Assert.True(await limiter.IsRateLimitedAsync("127.0.0.1", "user@test.com", "tenant-a"));

        await limiter.ResetAttemptsAsync("127.0.0.1", "user@test.com", "tenant-a");
        Assert.False(await limiter.IsRateLimitedAsync("127.0.0.1", "user@test.com", "tenant-a"));
    }
}
