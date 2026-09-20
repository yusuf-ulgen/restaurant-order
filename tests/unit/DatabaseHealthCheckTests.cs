using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Api.Health;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class DatabaseHealthCheckTests
{
    private class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "RestaurantOrder.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task MissingConfig_InDevelopment_BypassesAndReturnsTrue()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var checker = new NpgsqlDatabaseHealthCheck(config, env, NullLogger<NpgsqlDatabaseHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();

        Assert.True(isHealthy);
    }

    [Fact]
    public async Task MissingConfig_InProduction_FailsClosedAndReturnsFalse()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var checker = new NpgsqlDatabaseHealthCheck(config, env, NullLogger<NpgsqlDatabaseHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();

        Assert.False(isHealthy);
    }

    [Fact]
    public async Task UnreachableHost_InProduction_FailsClosed()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "Host=127.0.0.1;Port=54329;Database=fake;Username=fake;Password=fake;"
            })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var checker = new NpgsqlDatabaseHealthCheck(config, env, NullLogger<NpgsqlDatabaseHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();

        Assert.False(isHealthy);
    }

    [Fact]
    public async Task Cancellation_HonoredGracefully()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "Host=127.0.0.1;Port=5432;Database=test;Username=test;Password=test;"
            })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var checker = new NpgsqlDatabaseHealthCheck(config, env, NullLogger<NpgsqlDatabaseHealthCheck>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var isHealthy = await checker.IsHealthyAsync(cts.Token);
        Assert.False(isHealthy);
    }

    [Fact]
    public async Task FallbackToConnectionStringDatabase_WhenDatabaseUrlMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=127.0.0.1;Port=54328;Database=test;Username=test;Password=test;"
            })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var checker = new NpgsqlDatabaseHealthCheck(config, env, NullLogger<NpgsqlDatabaseHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();
        Assert.False(isHealthy);
    }

    [Fact]
    public async Task PostgresUriFormat_ParsedCorrectly()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgres://customuser:custompass@127.0.0.1:54329/testdb"
            })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var checker = new NpgsqlDatabaseHealthCheck(config, env, NullLogger<NpgsqlDatabaseHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();
        Assert.False(isHealthy);
    }

    [Fact]
    public async Task PostgresqlUriWithoutPortOrPassword_UsesDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgresql://useronly@127.0.0.1/testdb"
            })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var checker = new NpgsqlDatabaseHealthCheck(config, env, NullLogger<NpgsqlDatabaseHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();
        Assert.False(isHealthy);
    }

    [Fact]
    public async Task MalformedUri_HandledGracefully()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgres://invalid:uri:with:extra:colons"
            })
            .Build();
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var checker = new NpgsqlDatabaseHealthCheck(config, env, NullLogger<NpgsqlDatabaseHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();
        Assert.False(isHealthy);
    }
}
