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

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111-1111-1111-1111-111111111111", "00000000-0000-0000-0000-000000000000", "22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "00000000-0000-0000-0000-000000000000")]
    public async Task ValidateSessionActiveAsync_EmptyGuid_ReturnsFalseImmediately(string tenantStr, string branchStr, string sessionStr)
    {
        using var dbContext = CreateDummyDbContext();
        var validator = CreateValidator(dbContext);

        var result = await validator.ValidateSessionActiveAsync(Guid.Parse(tenantStr), Guid.Parse(branchStr), Guid.Parse(sessionStr));

        Assert.False(result);
        _redisDbMock.Verify(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task ValidateSessionActiveAsync_CacheHitActive_ReturnsTrueWithoutDbQuery()
    {
        using var dbContext = CreateDummyDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var expectedKey = $"customer_session:Test:{tenantId:D}:{branchId:D}:{sessionId:D}";

        _redisDbMock.Setup(d => d.StringGetAsync(expectedKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"1");

        var validator = CreateValidator(dbContext);
        var result = await validator.ValidateSessionActiveAsync(tenantId, branchId, sessionId);

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateSessionActiveAsync_CacheHitClosed_ReturnsFalseWithoutDbQuery()
    {
        using var dbContext = CreateDummyDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var expectedKey = $"customer_session:Test:{tenantId:D}:{branchId:D}:{sessionId:D}";

        _redisDbMock.Setup(d => d.StringGetAsync(expectedKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"0");

        var validator = CreateValidator(dbContext);
        var result = await validator.ValidateSessionActiveAsync(tenantId, branchId, sessionId);

        Assert.False(result);
    }

    [Fact]
    public async Task ValidateSessionActiveAsync_CacheMiss_DbFault_ReturnsFalseFailClosed()
    {
        using var dbContext = CreateDummyDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var validator = CreateValidator(dbContext);
        // DbContext without active DB connection will throw when attempting BeginTenantTransactionAsync,
        // which must be safely caught and return false (fail-closed).
        var result = await validator.ValidateSessionActiveAsync(tenantId, branchId, sessionId);

        Assert.False(result);
    }

    [Fact]
    public async Task InvalidateSessionCacheAsync_DeletesKeyFromRedis()
    {
        using var dbContext = CreateDummyDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var expectedKey = $"customer_session:Test:{tenantId:D}:{branchId:D}:{sessionId:D}";

        var validator = CreateValidator(dbContext);
        await validator.InvalidateSessionCacheAsync(tenantId, branchId, sessionId);

        _redisDbMock.Verify(d => d.KeyDeleteAsync(expectedKey, It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateSessionCacheAsync_EmptyGuid_DoesNothing()
    {
        using var dbContext = CreateDummyDbContext();
        var validator = CreateValidator(dbContext);
        await validator.InvalidateSessionCacheAsync(Guid.Empty, Guid.NewGuid(), Guid.NewGuid());

        _redisDbMock.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task InvalidateSessionCacheAsync_WhenRedisThrows_CatchesAndLogsWithoutThrowing()
    {
        using var dbContext = CreateDummyDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _redisProviderMock.Setup(r => r.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated Redis fault"));

        var validator = CreateValidator(dbContext);
        var exception = await Record.ExceptionAsync(() => validator.InvalidateSessionCacheAsync(tenantId, branchId, sessionId));

        Assert.Null(exception);
    }
}
