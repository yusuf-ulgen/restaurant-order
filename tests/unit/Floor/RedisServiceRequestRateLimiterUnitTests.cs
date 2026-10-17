using Microsoft.Extensions.Hosting;
using Moq;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Infrastructure.Floor;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class RedisServiceRequestRateLimiterUnitTests
{
    private readonly Mock<IRedisDatabaseProvider> _redisProviderMock = new();
    private readonly Mock<IDatabase> _databaseMock = new();
    private readonly Mock<IHostEnvironment> _environmentMock = new();

    public RedisServiceRequestRateLimiterUnitTests()
    {
        _environmentMock.Setup(e => e.EnvironmentName).Returns("Production");
        _redisProviderMock.Setup(r => r.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_databaseMock.Object);
    }

    [Fact]
    public async Task EnsureNotRateLimited_AllowedResult_CompletesSuccessfully()
    {
        _databaseMock.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(new RedisValue[] { 1, 0, "ok" }));

        var limiter = new RedisServiceRequestRateLimiter(_redisProviderMock.Object, _environmentMock.Object);

        await limiter.EnsureNotRateLimitedAsync(Guid.NewGuid(), Guid.NewGuid());
    }

    [Fact]
    public async Task EnsureNotRateLimited_Cooldown_ThrowsRateLimitException()
    {
        _databaseMock.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(new RedisValue[] { 0, 12, "cooldown" }));

        var limiter = new RedisServiceRequestRateLimiter(_redisProviderMock.Object, _environmentMock.Object);

        var ex = await Assert.ThrowsAsync<ServiceRequestRateLimitException>(() =>
            limiter.EnsureNotRateLimitedAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(12, ex.RetryAfterSeconds);
        Assert.Contains("wait", ex.Message);
    }

    [Fact]
    public async Task EnsureNotRateLimited_SlidingWindow_ThrowsRateLimitException()
    {
        _databaseMock.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(new RedisValue[] { 0, 45, "window" }));

        var limiter = new RedisServiceRequestRateLimiter(_redisProviderMock.Object, _environmentMock.Object);

        var ex = await Assert.ThrowsAsync<ServiceRequestRateLimitException>(() =>
            limiter.EnsureNotRateLimitedAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(45, ex.RetryAfterSeconds);
        Assert.Contains("rate limit exceeded", ex.Message);
    }

    [Fact]
    public async Task EnsureNotRateLimited_RedisFailsInProduction_ThrowsDistributedSecurityException_FailClosed()
    {
        _redisProviderMock.Setup(r => r.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis down"));

        var limiter = new RedisServiceRequestRateLimiter(_redisProviderMock.Object, _environmentMock.Object);

        await Assert.ThrowsAsync<DistributedSecurityStateUnavailableException>(() =>
            limiter.EnsureNotRateLimitedAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task EnsureNotRateLimited_RedisFailsInDevelopment_AllowsGracefulBypass()
    {
        _environmentMock.Setup(e => e.EnvironmentName).Returns("Development");
        _redisProviderMock.Setup(r => r.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis down"));

        var limiter = new RedisServiceRequestRateLimiter(_redisProviderMock.Object, _environmentMock.Object);

        // Should not throw in Development
        await limiter.EnsureNotRateLimitedAsync(Guid.NewGuid(), Guid.NewGuid());
    }
}
