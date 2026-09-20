using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Worker.Safety;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class RedisSafetyComponentsTests
{
    private class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "RestaurantOrder.Worker";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    // 1. RedisWorkerActivationGuard Tests
    [Fact]
    public async Task Guard_Production_FailsClosed_When_RedisUrl_Missing()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Production };
        var logger = NullLogger<RedisWorkerActivationGuard>.Instance;

        using var guard = new RedisWorkerActivationGuard(config, env, logger);

        Assert.Equal("unconfigured", guard.ActiveSlot);
        Assert.Equal(WorkerActivationStatus.Standby, guard.Status);

        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal(WorkerActivationStatus.Error, guard.Status);
        Assert.Equal("unconfigured", guard.ActiveSlot);
    }

    [Fact]
    public async Task Guard_Development_FallsBack_To_Configuration_When_RedisUrl_Missing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DEPLOYMENT_COLOR"] = "blue",
                ["ACTIVE_DEPLOYMENT_SLOT"] = "blue"
            })
            .Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Development };
        var logger = NullLogger<RedisWorkerActivationGuard>.Instance;

        using var guard = new RedisWorkerActivationGuard(config, env, logger);

        Assert.Equal("blue", guard.SlotColor);

        var isActive = await guard.IsActiveSlotAsync();

        Assert.True(isActive);
        Assert.Equal(WorkerActivationStatus.Active, guard.Status);
        Assert.Equal("blue", guard.ActiveSlot);
    }

    [Fact]
    public async Task Guard_Development_Standby_When_Slots_Mismatch()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DEPLOYMENT_COLOR"] = "green",
                ["ACTIVE_DEPLOYMENT_SLOT"] = "blue"
            })
            .Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Development };
        var logger = NullLogger<RedisWorkerActivationGuard>.Instance;

        using var guard = new RedisWorkerActivationGuard(config, env, logger);

        Assert.Equal("green", guard.SlotColor);

        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal(WorkerActivationStatus.Standby, guard.Status);
        Assert.Equal("blue", guard.ActiveSlot);
    }

    // 2. RedisWorkerLeaseManager Tests
    [Fact]
    public void LeaseManager_Generates_Unique_OwnerToken_When_Not_Provided()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Production };
        var logger = NullLogger<RedisWorkerLeaseManager>.Instance;

        using var manager1 = new RedisWorkerLeaseManager(config, env, logger);
        using var manager2 = new RedisWorkerLeaseManager(config, env, logger);

        Assert.NotEmpty(manager1.OwnerToken);
        Assert.NotEmpty(manager2.OwnerToken);
        Assert.NotEqual(manager1.OwnerToken, manager2.OwnerToken);
    }

    [Fact]
    public void LeaseManager_Preserves_Explicit_OwnerToken()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Production };
        var logger = NullLogger<RedisWorkerLeaseManager>.Instance;

        using var manager = new RedisWorkerLeaseManager(config, env, logger, null, "custom-worker-token-xyz");

        Assert.Equal("custom-worker-token-xyz", manager.OwnerToken);
    }

    [Fact]
    public async Task LeaseManager_Returns_False_When_Redis_Unavailable()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Production };
        var logger = NullLogger<RedisWorkerLeaseManager>.Instance;

        using var manager = new RedisWorkerLeaseManager(config, env, logger);

        var acquired = await manager.TryAcquireLeaseAsync("orders-lease", TimeSpan.FromSeconds(30));
        Assert.False(acquired);

        var renewed = await manager.RenewLeaseAsync("orders-lease", TimeSpan.FromSeconds(30));
        Assert.False(renewed);

        // ReleaseLeaseAsync does not throw when Redis unavailable
        await manager.ReleaseLeaseAsync("orders-lease");
    }

    // 3. RedisIdempotencyStore Tests
    [Fact]
    public async Task IdempotencyStore_Rejects_NullOrWhitespace_Key()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Production };
        var logger = NullLogger<RedisIdempotencyStore>.Instance;

        using var store = new RedisIdempotencyStore(config, env, logger);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.TryReserveKeyAsync("", TimeSpan.FromMinutes(1)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.TryReserveKeyAsync("   ", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task IdempotencyStore_Returns_False_When_Redis_Unavailable()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Production };
        var logger = NullLogger<RedisIdempotencyStore>.Instance;

        using var store = new RedisIdempotencyStore(config, env, logger);

        var reserved = await store.TryReserveKeyAsync("order-123", TimeSpan.FromMinutes(1));
        Assert.False(reserved);

        var isCompleted = await store.IsCompletedAsync("order-123");
        Assert.False(isCompleted);

        // MarkCompletedAsync does not throw
        await store.MarkCompletedAsync("order-123", TimeSpan.FromMinutes(1));

        // Null key handled gracefully
        await store.MarkCompletedAsync("", TimeSpan.FromMinutes(1));
        Assert.False(await store.IsCompletedAsync(""));
    }

    // 4. Mocked Redis operations for 100% deep branch coverage
    [Fact]
    public async Task Guard_WithMockRedis_ActiveSlot_Matches_ReturnsTrue()
    {
        var mockDb = new Moq.Mock<StackExchange.Redis.IDatabase>();
        mockDb.Setup(d => d.StringGetAsync(
            Moq.It.Is<StackExchange.Redis.RedisKey>(k => k == RedisWorkerActivationGuard.DefaultActiveSlotKey),
            Moq.It.IsAny<StackExchange.Redis.CommandFlags>()))
            .Returns(Task.FromResult(new StackExchange.Redis.RedisValue("green")));

        var mockMux = new Moq.Mock<StackExchange.Redis.IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(Moq.It.IsAny<int>(), Moq.It.IsAny<object>())).Returns(mockDb.Object);

        var logger = NullLogger<RedisWorkerActivationGuard>.Instance;
        using var guard = new RedisWorkerActivationGuard(mockMux.Object, logger, "green");

        var isActive = await guard.IsActiveSlotAsync();

        Assert.True(isActive);
        Assert.Equal("green", guard.ActiveSlot);
        Assert.Equal(WorkerActivationStatus.Active, guard.Status);
    }

    [Fact]
    public async Task Guard_WithMockRedis_ActiveSlot_Mismatches_ReturnsFalse()
    {
        var mockDb = new Moq.Mock<StackExchange.Redis.IDatabase>();
        mockDb.Setup(d => d.StringGetAsync(
            Moq.It.IsAny<StackExchange.Redis.RedisKey>(),
            Moq.It.IsAny<StackExchange.Redis.CommandFlags>()))
            .Returns(Task.FromResult(new StackExchange.Redis.RedisValue("blue")));

        var mockMux = new Moq.Mock<StackExchange.Redis.IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(Moq.It.IsAny<int>(), Moq.It.IsAny<object>())).Returns(mockDb.Object);

        var logger = NullLogger<RedisWorkerActivationGuard>.Instance;
        using var guard = new RedisWorkerActivationGuard(mockMux.Object, logger, "green");

        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal("blue", guard.ActiveSlot);
        Assert.Equal(WorkerActivationStatus.Standby, guard.Status);
    }

    [Fact]
    public async Task Guard_WithMockRedis_RedisThrows_ReturnsFalseAndErrorStatus()
    {
        var mockDb = new Moq.Mock<StackExchange.Redis.IDatabase>();
        mockDb.Setup(d => d.StringGetAsync(
            Moq.It.IsAny<StackExchange.Redis.RedisKey>(),
            Moq.It.IsAny<StackExchange.Redis.CommandFlags>()))
            .Returns(Task.FromException<StackExchange.Redis.RedisValue>(new StackExchange.Redis.RedisConnectionException(StackExchange.Redis.ConnectionFailureType.UnableToConnect, "test failure")));

        var mockMux = new Moq.Mock<StackExchange.Redis.IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(Moq.It.IsAny<int>(), Moq.It.IsAny<object>())).Returns(mockDb.Object);

        var logger = NullLogger<RedisWorkerActivationGuard>.Instance;
        using var guard = new RedisWorkerActivationGuard(mockMux.Object, logger, "blue");

        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal(WorkerActivationStatus.Error, guard.Status);
    }

    private class TestLogCollector<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task LeaseManager_WithMockRedis_AcquireAndRenew_Success()
    {
        var mockDb = new Moq.Mock<StackExchange.Redis.IDatabase>();
        mockDb.Setup(d => d.StringSetAsync(
            Moq.It.IsAny<StackExchange.Redis.RedisKey>(),
            Moq.It.IsAny<StackExchange.Redis.RedisValue>(),
            Moq.It.IsAny<TimeSpan?>(),
            Moq.It.IsAny<StackExchange.Redis.When>()))
            .Returns(Task.FromResult(true));

        mockDb.Setup(d => d.StringSetAsync(
            Moq.It.IsAny<StackExchange.Redis.RedisKey>(),
            Moq.It.IsAny<StackExchange.Redis.RedisValue>(),
            Moq.It.IsAny<TimeSpan?>(),
            Moq.It.IsAny<StackExchange.Redis.When>(),
            Moq.It.IsAny<StackExchange.Redis.CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockDb.Setup(d => d.ScriptEvaluateAsync(
            Moq.It.IsAny<string>(),
            Moq.It.IsAny<StackExchange.Redis.RedisKey[]>(),
            Moq.It.IsAny<StackExchange.Redis.RedisValue[]>(),
            Moq.It.IsAny<StackExchange.Redis.CommandFlags>()))
            .Returns(Task.FromResult(StackExchange.Redis.RedisResult.Create(1)));

        var mockMux = new Moq.Mock<StackExchange.Redis.IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(Moq.It.IsAny<int>(), Moq.It.IsAny<object?>())).Returns(mockDb.Object);

        var logger = NullLogger<RedisWorkerLeaseManager>.Instance;
        using var manager = new RedisWorkerLeaseManager(mockMux.Object, logger, "test-token");

        var acquired = await manager.TryAcquireLeaseAsync("test-lease", TimeSpan.FromSeconds(15));
        Assert.True(acquired);

        var renewed = await manager.RenewLeaseAsync("test-lease", TimeSpan.FromSeconds(15));
        Assert.True(renewed);

        await manager.ReleaseLeaseAsync("test-lease");
    }

    [Fact]
    public async Task IdempotencyStore_WithMockRedis_Operations_Succeed()
    {
        var mockDb = new Moq.Mock<StackExchange.Redis.IDatabase>();
        mockDb.Setup(d => d.StringSetAsync(
            Moq.It.IsAny<StackExchange.Redis.RedisKey>(),
            Moq.It.IsAny<StackExchange.Redis.RedisValue>(),
            Moq.It.IsAny<TimeSpan?>(),
            Moq.It.IsAny<StackExchange.Redis.When>()))
            .Returns(Task.FromResult(true));

        mockDb.Setup(d => d.StringSetAsync(
            Moq.It.IsAny<StackExchange.Redis.RedisKey>(),
            Moq.It.IsAny<StackExchange.Redis.RedisValue>(),
            Moq.It.IsAny<TimeSpan?>(),
            Moq.It.IsAny<StackExchange.Redis.When>(),
            Moq.It.IsAny<StackExchange.Redis.CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockDb.Setup(d => d.StringGetAsync(
            Moq.It.IsAny<StackExchange.Redis.RedisKey>(),
            Moq.It.IsAny<StackExchange.Redis.CommandFlags>()))
            .Returns(Task.FromResult(new StackExchange.Redis.RedisValue("COMPLETED")));

        var mockMux = new Moq.Mock<StackExchange.Redis.IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(Moq.It.IsAny<int>(), Moq.It.IsAny<object?>())).Returns(mockDb.Object);

        var logger = NullLogger<RedisIdempotencyStore>.Instance;
        using var store = new RedisIdempotencyStore(mockMux.Object, logger);

        var reserved = await store.TryReserveKeyAsync("idem-key-1", TimeSpan.FromMinutes(5));
        Assert.True(reserved);

        await store.MarkCompletedAsync("idem-key-1", TimeSpan.FromMinutes(5));

        var isDone = await store.IsCompletedAsync("idem-key-1");
        Assert.True(isDone);
    }

    [Fact]
    public async Task Guard_WhenRedisKeyMissingInProduction_FailsClosed()
    {
        var mockDb = new Moq.Mock<StackExchange.Redis.IDatabase>();
        mockDb.Setup(d => d.StringGetAsync(
            Moq.It.IsAny<StackExchange.Redis.RedisKey>(),
            Moq.It.IsAny<StackExchange.Redis.CommandFlags>()))
            .Returns(Task.FromResult(StackExchange.Redis.RedisValue.Null));

        var mockMux = new Moq.Mock<StackExchange.Redis.IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(Moq.It.IsAny<int>(), Moq.It.IsAny<object?>())).Returns(mockDb.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DEPLOYMENT_COLOR"] = "blue"
            })
            .Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Production };

        using var guard = new RedisWorkerActivationGuard(config, env, NullLogger<RedisWorkerActivationGuard>.Instance, mockMux.Object);
        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal(WorkerActivationStatus.Error, guard.Status);
        Assert.Equal("unconfigured", guard.ActiveSlot);
    }

    [Fact]
    public async Task Guard_WhenRedisSlotInvalid_FailsClosed()
    {
        var mockDb = new Moq.Mock<StackExchange.Redis.IDatabase>();
        mockDb.Setup(d => d.StringGetAsync(
            Moq.It.IsAny<StackExchange.Redis.RedisKey>(),
            Moq.It.IsAny<StackExchange.Redis.CommandFlags>()))
            .Returns(Task.FromResult(new StackExchange.Redis.RedisValue("yellow")));

        var mockMux = new Moq.Mock<StackExchange.Redis.IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(Moq.It.IsAny<int>(), Moq.It.IsAny<object?>())).Returns(mockDb.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DEPLOYMENT_COLOR"] = "blue"
            })
            .Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Production };

        using var guard = new RedisWorkerActivationGuard(config, env, NullLogger<RedisWorkerActivationGuard>.Instance, mockMux.Object);
        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal(WorkerActivationStatus.Error, guard.Status);
    }

    [Fact]
    public async Task Guard_WhenRedisThrowsException_FailsClosed()
    {
        var mockDb = new Moq.Mock<StackExchange.Redis.IDatabase>();
        mockDb.Setup(d => d.StringGetAsync(
            Moq.It.IsAny<StackExchange.Redis.RedisKey>(),
            Moq.It.IsAny<StackExchange.Redis.CommandFlags>()))
            .Throws(new Exception("Redis connection dropped"));

        var mockMux = new Moq.Mock<StackExchange.Redis.IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(Moq.It.IsAny<int>(), Moq.It.IsAny<object?>())).Returns(mockDb.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DEPLOYMENT_COLOR"] = "blue"
            })
            .Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Production };

        using var guard = new RedisWorkerActivationGuard(config, env, NullLogger<RedisWorkerActivationGuard>.Instance, mockMux.Object);
        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal(WorkerActivationStatus.Error, guard.Status);
    }

    [Fact]
    public void Guard_Development_WhenDeploymentColorMissing_DefaultsToBlue()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnv { EnvironmentName = Environments.Development };

        using var guard = new RedisWorkerActivationGuard(config, env, NullLogger<RedisWorkerActivationGuard>.Instance);
        Assert.Equal("blue", guard.SlotColor);
    }
}
