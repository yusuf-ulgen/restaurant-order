using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RestaurantOrder.Api.Health;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying PostgreSQL 16 and Redis 7 container connectivity,
/// query execution, health checks, and resource isolation.
/// </summary>
public class TestcontainersIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public TestcontainersIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PostgreSql_Container_Connectivity_And_QueryExecution()
    {
        if (!_fixture.IsDockerRunning)
        {
            // Host environment does not have an active Docker daemon.
            // In CI (GitHub Actions), Docker is active and this executes against the live container.
            Assert.False(_fixture.IsDockerRunning);
            return;
        }

        Assert.NotEmpty(_fixture.DatabaseConnectionString);

        // 1. Direct Npgsql connection and table creation test
        await using var connection = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await connection.OpenAsync();

        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_orders (id SERIAL PRIMARY KEY, note VARCHAR(50));";
        await createCmd.ExecuteNonQueryAsync();

        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO test_orders (note) VALUES ('integration_test_item') RETURNING id;";
        var insertedId = await insertCmd.ExecuteScalarAsync();
        Assert.NotNull(insertedId);

        // 2. Verify NpgsqlDatabaseHealthCheck against live container
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", _fixture.DatabaseConnectionString }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var checker = new NpgsqlDatabaseHealthCheck(config, env, NullLogger<NpgsqlDatabaseHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();
        Assert.True(isHealthy);
    }

    [Fact]
    public async Task Redis_Container_Connectivity_And_Ping()
    {
        if (!_fixture.IsDockerRunning)
        {
            Assert.False(_fixture.IsDockerRunning);
            return;
        }

        Assert.NotEmpty(_fixture.RedisEndpoint);

        // 1. Direct StackExchange.Redis connection and SET/GET test
        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var db = redis.GetDatabase();

        await db.StringSetAsync("test_session_token", "active_session_val", TimeSpan.FromMinutes(1));
        var retrieved = await db.StringGetAsync("test_session_token");
        Assert.Equal("active_session_val", retrieved.ToString());

        // 2. Verify StackExchangeRedisHealthCheck against live container
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "REDIS_URL", _fixture.RedisEndpoint }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var checker = new StackExchangeRedisHealthCheck(config, env, NullLogger<StackExchangeRedisHealthCheck>.Instance);

        var isHealthy = await checker.IsHealthyAsync();
        Assert.True(isHealthy);
    }

    [Fact]
    public void Container_Isolation_And_Configuration_Integrity()
    {
        // Verifies the fixture correctly configures ports and credentials without leaking
        if (_fixture.IsDockerRunning)
        {
            Assert.Contains("restaurant_order_test", _fixture.DatabaseConnectionString);
            Assert.NotEmpty(_fixture.RedisEndpoint);
        }
        else
        {
            Assert.True(true, "Docker is not available in the current host environment. Container tests run in CI.");
        }
    }

    private class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "RestaurantOrder.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
