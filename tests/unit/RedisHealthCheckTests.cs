using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Api.Health;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class RedisHealthCheckTests
{
    private class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "RestaurantOrder.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private class TestLogger<T> : ILogger<T>
    {
        public List<string> LoggedMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LoggedMessages.Add(formatter(state, exception));
        }
    }

    private class FakeRedisConnectionProvider : IRedisConnectionProvider
    {
        public int CallCount { get; private set; }
        public IConnectionMultiplexer? ConnectionToReturn { get; set; }
        public bool Disposed { get; private set; }

        public Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(ConnectionToReturn);
        }

        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task MissingConfig_InDevelopment_BypassesAndReturnsTrue()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var fakeProvider = new FakeRedisConnectionProvider();
        var checker = new StackExchangeRedisHealthCheck(fakeProvider, config, env, NullLogger<StackExchangeRedisHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();

        Assert.True(isHealthy);
        Assert.Equal(0, fakeProvider.CallCount); // Provider not called when config absent in dev
    }

    [Fact]
    public async Task MissingConfig_InProduction_FailsClosedAndReturnsFalse()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var fakeProvider = new FakeRedisConnectionProvider();
        var logger = new TestLogger<StackExchangeRedisHealthCheck>();
        var checker = new StackExchangeRedisHealthCheck(fakeProvider, config, env, logger);

        var isHealthy = await checker.IsHealthyAsync();

        Assert.False(isHealthy);
        Assert.Equal(0, fakeProvider.CallCount);
        Assert.Contains(logger.LoggedMessages, m => m.Contains("failed closed"));
    }

    [Fact]
    public async Task NullConnection_InDevelopment_ReturnsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["REDIS_URL"] = "redis://localhost:6379" })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var fakeProvider = new FakeRedisConnectionProvider { ConnectionToReturn = null };
        var checker = new StackExchangeRedisHealthCheck(fakeProvider, config, env, NullLogger<StackExchangeRedisHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();

        Assert.True(isHealthy);
        Assert.Equal(1, fakeProvider.CallCount);
    }

    [Fact]
    public async Task NullConnection_InProduction_FailsClosed()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["REDIS_URL"] = "redis://localhost:6379" })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var fakeProvider = new FakeRedisConnectionProvider { ConnectionToReturn = null };
        var checker = new StackExchangeRedisHealthCheck(fakeProvider, config, env, NullLogger<StackExchangeRedisHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();

        Assert.False(isHealthy);
        Assert.Equal(1, fakeProvider.CallCount);
    }

    [Fact]
    public async Task ReusesConnection_AcrossMultipleProbes()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["REDIS_URL"] = "redis://non-existent-host:6379" })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var logger = new TestLogger<StackExchangeRedisConnectionProvider>();
        using var provider = new StackExchangeRedisConnectionProvider(config, env, logger);

        // Multiple calls to provider should return the exact same multiplexer reference (singleton reuse)
        var conn1 = await provider.GetConnectionAsync();
        var conn2 = await provider.GetConnectionAsync();

        Assert.NotNull(conn1);
        Assert.Same(conn1, conn2);
    }

    [Fact]
    public async Task SanitizesExceptionLogs_NeverLeaksEndpointsOrCredentials()
    {
        var secretPassword = "superSecretPassword123!";
        var internalHost = "internal-db.corp.local:6379";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REDIS_URL"] = $"redis://user:{secretPassword}@{internalHost}"
            })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var logger = new TestLogger<StackExchangeRedisConnectionProvider>();
        using var provider = new StackExchangeRedisConnectionProvider(config, env, logger);

        await provider.GetConnectionAsync();

        // Ensure no logged message contains password or internal host
        foreach (var msg in logger.LoggedMessages)
        {
            Assert.DoesNotContain(secretPassword, msg);
            Assert.DoesNotContain(internalHost, msg);
        }
    }

    [Fact]
    public async Task Cancellation_HonoredGracefully()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["REDIS_URL"] = "redis://localhost:6379" })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var fakeProvider = new FakeRedisConnectionProvider();
        var checker = new StackExchangeRedisHealthCheck(fakeProvider, config, env, NullLogger<StackExchangeRedisHealthCheck>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var isHealthy = await checker.IsHealthyAsync(cts.Token);
        Assert.False(isHealthy);
    }

    [Fact]
    public async Task ConnectionProvider_WhenConfigMissing_BehavesPerEnvironment()
    {
        var config = new ConfigurationBuilder().Build();
        var devEnv = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var prodEnv = new FakeHostEnvironment { EnvironmentName = Environments.Production };

        using var devProvider = new StackExchangeRedisConnectionProvider(config, devEnv, NullLogger<StackExchangeRedisConnectionProvider>.Instance);
        var devConn = await devProvider.GetConnectionAsync();
        Assert.Null(devConn);

        using var prodProvider = new StackExchangeRedisConnectionProvider(config, prodEnv, NullLogger<StackExchangeRedisConnectionProvider>.Instance);
        var prodConn = await prodProvider.GetConnectionAsync();
        Assert.Null(prodConn);
    }

    [Fact]
    public async Task ConnectionProvider_FallbackToConnectionStringRedis()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "127.0.0.1:63799,abortConnect=false"
            })
            .Build();
        var prodEnv = new FakeHostEnvironment { EnvironmentName = Environments.Production };

        using var provider = new StackExchangeRedisConnectionProvider(config, prodEnv, NullLogger<StackExchangeRedisConnectionProvider>.Instance);
        var conn = await provider.GetConnectionAsync();
        Assert.NotNull(conn);
        Assert.False(conn.IsConnected);
    }

    [Fact]
    public async Task ConnectionProvider_ThrowsWhenDisposed()
    {
        var config = new ConfigurationBuilder().Build();
        var devEnv = new FakeHostEnvironment { EnvironmentName = Environments.Development };

        var provider = new StackExchangeRedisConnectionProvider(config, devEnv, NullLogger<StackExchangeRedisConnectionProvider>.Instance);
        provider.Dispose();
        provider.Dispose(); // Multiple dispose safe

        await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetConnectionAsync());

        await provider.DisposeAsync(); // Async dispose safe
    }
}
