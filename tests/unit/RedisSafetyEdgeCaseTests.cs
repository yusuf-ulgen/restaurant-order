using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Worker.Safety;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class RedisSafetyEdgeCaseTests
{
    private class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "RestaurantOrder.Worker";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Fact]
    public async Task LeaseManager_TryAcquireLeaseAsync_WhenDbFails_ReturnsFalse()
    {
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var mockMux = new Mock<IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(mockDb.Object);

        using var manager = new RedisWorkerLeaseManager(mockMux.Object, NullLogger<RedisWorkerLeaseManager>.Instance, "token");
        var acquired = await manager.TryAcquireLeaseAsync("lease-key", TimeSpan.FromSeconds(10));
        Assert.False(acquired);
    }

    [Fact]
    public async Task LeaseManager_TryAcquireLeaseAsync_WhenExceptionThrown_ReturnsFalse()
    {
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failure"));

        var mockMux = new Mock<IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(mockDb.Object);

        using var manager = new RedisWorkerLeaseManager(mockMux.Object, NullLogger<RedisWorkerLeaseManager>.Instance, "token");
        var acquired = await manager.TryAcquireLeaseAsync("lease-key", TimeSpan.FromSeconds(10));
        Assert.False(acquired);
    }

    [Fact]
    public async Task LeaseManager_RenewLeaseAsync_WhenScriptReturnsZero_ReturnsFalse()
    {
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(0));

        var mockMux = new Mock<IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(mockDb.Object);

        using var manager = new RedisWorkerLeaseManager(mockMux.Object, NullLogger<RedisWorkerLeaseManager>.Instance, "token");
        var renewed = await manager.RenewLeaseAsync("lease-key", TimeSpan.FromSeconds(10));
        Assert.False(renewed);
    }

    [Fact]
    public async Task LeaseManager_RenewLeaseAsync_WhenExceptionThrown_ReturnsFalse()
    {
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisTimeoutException("Timeout", CommandStatus.Unknown));

        var mockMux = new Mock<IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(mockDb.Object);

        using var manager = new RedisWorkerLeaseManager(mockMux.Object, NullLogger<RedisWorkerLeaseManager>.Instance, "token");
        var renewed = await manager.RenewLeaseAsync("lease-key", TimeSpan.FromSeconds(10));
        Assert.False(renewed);
    }

    [Fact]
    public async Task LeaseManager_ReleaseLeaseAsync_HandlesZeroAndException()
    {
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(0));

        var mockMux = new Mock<IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(mockDb.Object);

        using var manager = new RedisWorkerLeaseManager(mockMux.Object, NullLogger<RedisWorkerLeaseManager>.Instance, "token");
        await manager.ReleaseLeaseAsync("lease-key");

        // Now with exception
        mockDb.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new Exception("Network failure"));

        await manager.ReleaseLeaseAsync("lease-key");
    }

    [Fact]
    public async Task IdempotencyStore_Validations_And_ErrorBranches()
    {
        var mockMux = new Mock<IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);

        using var store = new RedisIdempotencyStore(mockMux.Object, NullLogger<RedisIdempotencyStore>.Instance);

        // Null / whitespace key check
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryReserveKeyAsync("", TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryReserveKeyAsync("   ", TimeSpan.FromMinutes(1)));

        // Empty key for MarkCompletedAsync and IsCompletedAsync
        await store.MarkCompletedAsync("", TimeSpan.FromMinutes(1));
        var completed = await store.IsCompletedAsync("");
        Assert.False(completed);

        // When DB returns false on TryReserveKeyAsync
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        mockMux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(mockDb.Object);

        var reserved = await store.TryReserveKeyAsync("dup-key", TimeSpan.FromMinutes(1));
        Assert.False(reserved);

        // When DB throws on StringGetAsync
        mockDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new Exception("Redis down"));

        var isDone = await store.IsCompletedAsync("some-key");
        Assert.False(isDone);
    }

    [Fact]
    public async Task Components_FailClosed_WhenRedisUnconfigured()
    {
        var emptyConfig = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Production };

        using var lease = new RedisWorkerLeaseManager(emptyConfig, env, NullLogger<RedisWorkerLeaseManager>.Instance);
        var acquired = await lease.TryAcquireLeaseAsync("k", TimeSpan.FromSeconds(5));
        Assert.False(acquired);

        using var store = new RedisIdempotencyStore(emptyConfig, env, NullLogger<RedisIdempotencyStore>.Instance);
        var reserved = await store.TryReserveKeyAsync("k", TimeSpan.FromSeconds(5));
        Assert.False(reserved);
        var isDone = await store.IsCompletedAsync("k");
        Assert.False(isDone);
    }
}
