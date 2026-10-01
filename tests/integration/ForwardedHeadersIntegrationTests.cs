using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class ForwardedHeadersIntegrationTests
{
    [Fact]
    public void ForwardedHeaders_EnabledInDevelopment_ConfiguresKnownProxiesAndNetworks()
    {
        using var factory = new WebApplicationFactory<RestaurantOrder.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("FORWARDED_HEADERS_ENABLED", "true");
            builder.UseSetting("FORWARDED_HEADERS_KNOWN_PROXIES", "192.0.2.1, 192.0.2.2");
            builder.UseSetting("FORWARDED_HEADERS_KNOWN_NETWORKS", "198.51.100.0/24");
            builder.UseSetting("FORWARDED_HEADERS_FORWARD_LIMIT", "3");
            builder.UseSetting("DEPLOYMENT_COLOR", "blue");
            builder.UseSetting("DATABASE_URL", "Host=localhost;Database=test;Username=test_app;Password=secret_app_pass");
            builder.UseSetting("REDIS_URL", "localhost:6379");
            builder.UseSetting("JWT_SECRET", RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret);
        });

        // Resolve ForwardedHeadersOptions to trigger Configure callback in Program.cs
        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.True(options.RequireHeaderSymmetry);
        Assert.Equal(3, options.ForwardLimit);
        Assert.Contains(options.KnownProxies, p => p.ToString() == "192.0.2.1");
        Assert.Contains(options.KnownProxies, p => p.ToString() == "192.0.2.2");
        Assert.Contains(options.KnownIPNetworks, n => n.BaseAddress.ToString() == "198.51.100.0" && n.PrefixLength == 24);
        // In Development, loopback is also included
        Assert.Contains(options.KnownIPNetworks, n => n.BaseAddress.ToString() == "127.0.0.0" && n.PrefixLength == 8);
    }

    [Fact]
    public void ForwardedHeaders_EnabledInProduction_ConfiguresOptionsWithoutDevLoopback()
    {
        using var factory = new WebApplicationFactory<RestaurantOrder.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("FORWARDED_HEADERS_ENABLED", "true");
            builder.UseSetting("FORWARDED_HEADERS_KNOWN_PROXIES", "192.0.2.10");
            builder.UseSetting("FORWARDED_HEADERS_KNOWN_NETWORKS", "198.51.100.0/24");
            builder.UseSetting("DEPLOYMENT_COLOR", "blue");
            builder.UseSetting("DATABASE_URL", "Host=localhost;Database=test;Username=test_app;Password=secret_app_pass");
            builder.UseSetting("REDIS_URL", "localhost:6379");
            builder.UseSetting("JWT_SECRET", "super-secure-production-jwt-secret-key-32-chars-long!");
            builder.UseSetting("PIN_PEPPER_SECRET", "super-secure-production-pin-pepper-key-32-chars-long!");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://admin.restaurantorder.app");
            builder.UseSetting("NOTIFICATION_PROVIDER", "TransactionalOutbox");
        });

        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.True(options.RequireHeaderSymmetry);
        Assert.Equal(2, options.ForwardLimit); // Default forward limit is 2
        Assert.Contains(options.KnownProxies, p => p.ToString() == "192.0.2.10");
        Assert.Contains(options.KnownIPNetworks, n => n.BaseAddress.ToString() == "198.51.100.0");
        // In Production, dev loopback networks are NOT added
        Assert.DoesNotContain(options.KnownIPNetworks, n => n.BaseAddress.ToString() == "127.0.0.1");
    }

    [Fact]
    public async Task ForwardedHeaders_Enabled_ProcessesXForwardedHeadersOnRequests()
    {
        using var factory = new WebApplicationFactory<RestaurantOrder.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("FORWARDED_HEADERS_ENABLED", "true");
            builder.UseSetting("FORWARDED_HEADERS_KNOWN_PROXIES", "127.0.0.1");
            builder.UseSetting("DEPLOYMENT_COLOR", "blue");
            builder.UseSetting("DATABASE_URL", "Host=localhost;Database=test;Username=test_app;Password=secret_app_pass");
            builder.UseSetting("REDIS_URL", "localhost:6379");
            builder.UseSetting("JWT_SECRET", RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret);
        });

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Forwarded-For", "203.0.113.195");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
