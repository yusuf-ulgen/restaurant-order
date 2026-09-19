using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

using RestaurantOrder.Api;
using RestaurantOrder.Api.Health;

namespace RestaurantOrder.IntegrationTests;

public class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private class TestDatabaseHealthCheck : IDatabaseHealthCheck
    {
        private readonly bool _isHealthy;
        public TestDatabaseHealthCheck(bool isHealthy) => _isHealthy = isHealthy;
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(_isHealthy);
    }

    private class TestRedisHealthCheck : IRedisHealthCheck
    {
        private readonly bool _isHealthy;
        public TestRedisHealthCheck(bool isHealthy) => _isHealthy = isHealthy;
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(_isHealthy);
    }

    private class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "RestaurantOrder.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private HttpClient CreateCustomClient(bool dbHealthy, bool redisHealthy)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDatabaseHealthCheck));
                if (dbDescriptor != null) services.Remove(dbDescriptor);
                services.AddSingleton<IDatabaseHealthCheck>(new TestDatabaseHealthCheck(dbHealthy));

                var redisDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IRedisHealthCheck));
                if (redisDescriptor != null) services.Remove(redisDescriptor);
                services.AddSingleton<IRedisHealthCheck>(new TestRedisHealthCheck(redisHealthy));
            });
        }).CreateClient();
    }

    [Fact]
    public async Task HealthLive_ReturnsOk_WithHealthyStatus()
    {
        var client = CreateCustomClient(dbHealthy: true, redisHealthy: true);
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", content.GetProperty("status").GetString());
        Assert.Equal("restaurant-order-api", content.GetProperty("service").GetString());
        Assert.Equal("0.1.0", content.GetProperty("version").GetString());
    }

    [Fact]
    public async Task HealthReady_WhenBothDependenciesHealthy_ReturnsOk()
    {
        var client = CreateCustomClient(dbHealthy: true, redisHealthy: true);
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", content.GetProperty("status").GetString());
        Assert.Equal("Healthy", content.GetProperty("checks").GetProperty("database").GetString());
        Assert.Equal("Healthy", content.GetProperty("checks").GetProperty("redis").GetString());

        // Ensure sensitive connection string details are never leaked
        var rawJson = content.ToString();
        Assert.DoesNotContain("Host=", rawJson);
        Assert.DoesNotContain("Password=", rawJson);
        Assert.DoesNotContain("User", rawJson);
    }

    [Fact]
    public async Task HealthReady_WhenDatabaseFails_ReturnsServiceUnavailable_AndUnhealthyStatus()
    {
        var client = CreateCustomClient(dbHealthy: false, redisHealthy: true);
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unhealthy", content.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", content.GetProperty("checks").GetProperty("database").GetString());
        Assert.Equal("Healthy", content.GetProperty("checks").GetProperty("redis").GetString());
    }

    [Fact]
    public async Task HealthReady_WhenRedisFails_ReturnsServiceUnavailable_AndUnhealthyStatus()
    {
        var client = CreateCustomClient(dbHealthy: true, redisHealthy: false);
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unhealthy", content.GetProperty("status").GetString());
        Assert.Equal("Healthy", content.GetProperty("checks").GetProperty("database").GetString());
        Assert.Equal("Unhealthy", content.GetProperty("checks").GetProperty("redis").GetString());
    }

    [Fact]
    public async Task HealthLive_WhenBothDependenciesFail_ContinuesToReturnOk()
    {
        // Liveness must remain completely decoupled from external infrastructure failures
        var client = CreateCustomClient(dbHealthy: false, redisHealthy: false);
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", content.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RealNpgsqlDatabaseHealthCheck_WhenHostUnreachable_FailsClosed()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=127.0.0.1;Port=54329;Database=test;Username=postgres;Password=test;Timeout=1;CommandTimeout=1" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var logger = new LoggerFactory().CreateLogger<NpgsqlDatabaseHealthCheck>();
        var checker = new NpgsqlDatabaseHealthCheck(config, env, logger);

        var isHealthy = await checker.IsHealthyAsync();
        Assert.False(isHealthy);
    }

    [Fact]
    public async Task RealStackExchangeRedisHealthCheck_WhenHostUnreachable_FailsClosed()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "REDIS_URL", "127.0.0.1:54329" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var logger = new LoggerFactory().CreateLogger<StackExchangeRedisHealthCheck>();
        var checker = new StackExchangeRedisHealthCheck(config, env, logger);

        var isHealthy = await checker.IsHealthyAsync();
        Assert.False(isHealthy);
    }
}
