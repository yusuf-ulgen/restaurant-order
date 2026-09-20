using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantOrder.Api;
using RestaurantOrder.Api.Health;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class HealthEndpointsTests
{
    private class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly Action<IServiceCollection>? _configureServices;
        private readonly IDictionary<string, string?>? _configurationOverrides;

        public TestApiFactory(
            Action<IServiceCollection>? configureServices = null,
            IDictionary<string, string?>? configurationOverrides = null)
        {
            _configureServices = configureServices;
            _configurationOverrides = configurationOverrides;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            if (_configurationOverrides != null)
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(_configurationOverrides);
                });
            }

            builder.ConfigureServices(services =>
            {
                _configureServices?.Invoke(services);
            });
        }
    }

    [Fact]
    public async Task HealthLive_ReturnsOk_WithHealthyStatusAndMetadata()
    {
        var dbMock = new Mock<IDatabaseHealthCheck>();
        var redisMock = new Mock<IRedisHealthCheck>();

        using var factory = new TestApiFactory(services =>
        {
            services.AddSingleton(dbMock.Object);
            services.AddSingleton(redisMock.Object);
        }, new Dictionary<string, string?>
        {
            ["DEPLOYMENT_COLOR"] = "blue"
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<HealthLiveResponse>();
        Assert.NotNull(content);
        Assert.Equal("Healthy", content.Status);
        Assert.Equal("restaurant-order-api", content.Service);
        Assert.Equal("0.1.0", content.Version);
        Assert.Equal("blue", content.Color);
    }

    [Fact]
    public async Task HealthReady_WhenAllHealthy_ReturnsOk()
    {
        var dbMock = new Mock<IDatabaseHealthCheck>();
        dbMock.Setup(d => d.IsHealthyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var redisMock = new Mock<IRedisHealthCheck>();
        redisMock.Setup(r => r.IsHealthyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        using var factory = new TestApiFactory(services =>
        {
            services.AddSingleton(dbMock.Object);
            services.AddSingleton(redisMock.Object);
        }, new Dictionary<string, string?>
        {
            ["DEPLOYMENT_COLOR"] = "green"
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<HealthReadyResponse>();
        Assert.NotNull(content);
        Assert.Equal("Healthy", content.Status);
        Assert.Equal("green", content.Color);
        Assert.Equal("Healthy", content.Checks.Database);
        Assert.Equal("Healthy", content.Checks.Redis);
    }

    [Fact]
    public async Task HealthReady_WhenDatabaseUnhealthy_Returns503ServiceUnavailable()
    {
        var dbMock = new Mock<IDatabaseHealthCheck>();
        dbMock.Setup(d => d.IsHealthyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var redisMock = new Mock<IRedisHealthCheck>();
        redisMock.Setup(r => r.IsHealthyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        using var factory = new TestApiFactory(services =>
        {
            services.AddSingleton(dbMock.Object);
            services.AddSingleton(redisMock.Object);
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<HealthReadyResponse>();
        Assert.NotNull(content);
        Assert.Equal("Unhealthy", content.Status);
        Assert.Equal("Unhealthy", content.Checks.Database);
        Assert.Equal("Healthy", content.Checks.Redis);
    }

    [Fact]
    public async Task HealthReady_WhenRedisUnhealthy_Returns503ServiceUnavailable()
    {
        var dbMock = new Mock<IDatabaseHealthCheck>();
        dbMock.Setup(d => d.IsHealthyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var redisMock = new Mock<IRedisHealthCheck>();
        redisMock.Setup(r => r.IsHealthyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        using var factory = new TestApiFactory(services =>
        {
            services.AddSingleton(dbMock.Object);
            services.AddSingleton(redisMock.Object);
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<HealthReadyResponse>();
        Assert.NotNull(content);
        Assert.Equal("Unhealthy", content.Status);
        Assert.Equal("Healthy", content.Checks.Database);
        Assert.Equal("Unhealthy", content.Checks.Redis);
    }

    [Fact]
    public async Task HealthReady_WhenBothUnhealthy_Returns503ServiceUnavailable()
    {
        var dbMock = new Mock<IDatabaseHealthCheck>();
        dbMock.Setup(d => d.IsHealthyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var redisMock = new Mock<IRedisHealthCheck>();
        redisMock.Setup(r => r.IsHealthyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        using var factory = new TestApiFactory(services =>
        {
            services.AddSingleton(dbMock.Object);
            services.AddSingleton(redisMock.Object);
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<HealthReadyResponse>();
        Assert.NotNull(content);
        Assert.Equal("Unhealthy", content.Status);
        Assert.Equal("Unhealthy", content.Checks.Database);
        Assert.Equal("Unhealthy", content.Checks.Redis);
    }
}
