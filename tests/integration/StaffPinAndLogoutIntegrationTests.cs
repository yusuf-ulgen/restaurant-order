using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class StaffPinAndLogoutIntegrationTests
{
    private static HttpClient CreateClient(
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new WebApplicationFactory<RestaurantOrder.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DEPLOYMENT_COLOR", "blue");
            builder.UseSetting("DATABASE_URL", "Host=localhost;Database=test;Username=test_app;Password=secret_app_pass");
            builder.UseSetting("REDIS_URL", "localhost:6379");
            builder.UseSetting("JWT_SECRET", RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret);
            builder.ConfigureServices(services =>
            {
                configureServices?.Invoke(services);
            });
        });

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
    }

    [Fact]
    public async Task PinLogout_WithSessionIdAndTerminal_ClearsAuthCookiesAndReturns200()
    {
        var mockPinService = new Mock<IStaffPinAuthService>();
        var terminalId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var client = CreateClient(services =>
        {
            services.AddScoped(_ => mockPinService.Object);
        });

        var terminalCookie = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{terminalId:D}:sample-token-fixture-abc"));
        var csrfToken = "csrf_token_test_1234567890123456";
        client.DefaultRequestHeaders.Add("Cookie", $"{AuthCookieService.TerminalCredCookieName}={terminalCookie}; {AuthCookieService.CsrfTokenCookieName}={csrfToken}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", csrfToken);

        var response = await client.PostAsync("/api/v1/auth/pin/logout", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.AccessTokenCookieName) && c.Contains("expires="));
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.CsrfTokenCookieName));
    }

    [Fact]
    public async Task PinLogin_WhenRateLimited_Returns429WithRetryAfter()
    {
        var mockPinService = new Mock<IStaffPinAuthService>();
        mockPinService.Setup(s => s.LoginWithPinAsync(It.IsAny<PinLoginCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthRateLimitException(30, "Too many PIN attempts"));

        var client = CreateClient(services =>
        {
            services.AddScoped(_ => mockPinService.Object);
        });

        var terminalId = Guid.NewGuid();
        var terminalCookie = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{terminalId:D}:sample-token-fixture-abc"));
        var csrfToken = "csrf_token_test_1234567890123456";
        client.DefaultRequestHeaders.Add("Cookie", $"{AuthCookieService.TerminalCredCookieName}={terminalCookie}; {AuthCookieService.CsrfTokenCookieName}={csrfToken}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", csrfToken);

        var request = new PinLoginApiRequest(Guid.NewGuid(), null, "1234");
        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/login", request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task PinLogin_WithoutTerminalCredentials_Returns401()
    {
        var client = CreateClient();
        var request = new PinLoginApiRequest(Guid.NewGuid(), null, "1234");
        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
