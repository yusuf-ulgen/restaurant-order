using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Infrastructure.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class CustomerSessionValidatorUnitTests
{
    private readonly Mock<IRedisDatabaseProvider> _redisProviderMock = new();
    private readonly Mock<IDatabase> _redisDbMock = new();
    private readonly Mock<IHostEnvironment> _envMock = new();

    public CustomerSessionValidatorUnitTests()
    {
        _envMock.Setup(e => e.EnvironmentName).Returns("Test");
        _redisProviderMock.Setup(r => r.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_redisDbMock.Object);
    }

    private CustomerSessionValidator CreateValidator(RestaurantOrderDbContext dbContext)
    {
        return new CustomerSessionValidator(
            dbContext,
            _redisProviderMock.Object,
            _envMock.Object,
            NullLogger<CustomerSessionValidator>.Instance);
    }

    private static RestaurantOrderDbContext CreateDummyDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=postgres;Password=dummy")
            .Options;
        return new RestaurantOrderDbContext(options, new TenantContext(Guid.NewGuid(), isAuthenticated: true));
    }

    [Fact]
    public async Task ValidateSessionActiveAsync_EmptyGuid_ReturnsFalseImmediately()
    {
        using var dbContext = CreateDummyDbContext();
        var validator = CreateValidator(dbContext);

        var result = await validator.ValidateSessionActiveAsync(Guid.Empty);

        Assert.False(result);
        _redisDbMock.Verify(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task ValidateSessionActiveAsync_CacheHitActive_ReturnsTrueWithoutDbQuery()
    {
        using var dbContext = CreateDummyDbContext();
        var sessionId = Guid.NewGuid();
        var expectedKey = $"customer_session:Test:{sessionId:D}";

        _redisDbMock.Setup(d => d.StringGetAsync(expectedKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"1");

        var validator = CreateValidator(dbContext);
        var result = await validator.ValidateSessionActiveAsync(sessionId);

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateSessionActiveAsync_CacheHitClosed_ReturnsFalseWithoutDbQuery()
    {
        using var dbContext = CreateDummyDbContext();
        var sessionId = Guid.NewGuid();
        var expectedKey = $"customer_session:Test:{sessionId:D}";

        _redisDbMock.Setup(d => d.StringGetAsync(expectedKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"0");

        var validator = CreateValidator(dbContext);
        var result = await validator.ValidateSessionActiveAsync(sessionId);

        Assert.False(result);
    }

    [Fact]
    public async Task InvalidateSessionCacheAsync_DeletesKeyFromRedis()
    {
        using var dbContext = CreateDummyDbContext();
        var sessionId = Guid.NewGuid();
        var expectedKey = $"customer_session:Test:{sessionId:D}";

        var validator = CreateValidator(dbContext);
        await validator.InvalidateSessionCacheAsync(sessionId);

        _redisDbMock.Verify(d => d.KeyDeleteAsync(expectedKey, It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateSessionCacheAsync_EmptyGuid_DoesNothing()
    {
        using var dbContext = CreateDummyDbContext();
        var validator = CreateValidator(dbContext);
        await validator.InvalidateSessionCacheAsync(Guid.Empty);

        _redisDbMock.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task InvalidateSessionCacheAsync_WhenRedisThrows_CatchesAndLogsWithoutThrowing()
    {
        using var dbContext = CreateDummyDbContext();
        var sessionId = Guid.NewGuid();

        _redisProviderMock.Setup(r => r.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated Redis fault"));

        var validator = CreateValidator(dbContext);
        var exception = await Record.ExceptionAsync(() => validator.InvalidateSessionCacheAsync(sessionId));

        Assert.Null(exception);
    }
}
