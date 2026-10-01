using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class AuthEndpointsIntegrationTests : IClassFixture<WebApplicationFactory<RestaurantOrder.Api.Program>>
{
    private readonly WebApplicationFactory<RestaurantOrder.Api.Program> _factory;

    public AuthEndpointsIntegrationTests(WebApplicationFactory<RestaurantOrder.Api.Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithAuthService(Mock<IAuthService> mockAuthService)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddScoped(_ => mockAuthService.Object);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false // We inspect cookies directly
        });
    }

    [Fact]
    public async Task Login_ValidCredentials_SetsHttpOnlyCookies_AndReturns200()
    {
        var mockAuth = new Mock<IAuthService>();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var authResult = new AuthResult(
            AccessToken: "test.jwt.token",
            RefreshToken: "test_refresh_token_64_chars_long_opaque_value_1234567890abcdef12345678",
            AccessTokenExpiresAt: now.AddMinutes(10),
            RefreshTokenExpiresAt: now.AddDays(7),
            User: new UserPrincipalDto(userId, "admin@test.com", "RestaurantAdmin", Guid.NewGuid(), null, 1),
            Session: new SessionDto(sessionId, "Password", "Active", now, now, now.AddDays(7), IsCurrent: true));

        mockAuth.Setup(a => a.LoginAsync(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        var client = CreateClientWithAuthService(mockAuth);

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@test.com", "CorrectPassword123!", "test-tenant"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify cookies
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();

        // 1. Access Token Cookie
        var accessCookie = cookies.FirstOrDefault(c => c.Contains(AuthCookieService.AccessTokenCookieName));
        Assert.NotNull(accessCookie);
        Assert.Contains("httponly", accessCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", accessCookie, StringComparison.OrdinalIgnoreCase);

        // 2. Refresh Token Cookie
        var refreshCookie = cookies.FirstOrDefault(c => c.Contains(AuthCookieService.RefreshTokenCookieName));
        Assert.NotNull(refreshCookie);
        Assert.Contains("httponly", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", refreshCookie, StringComparison.OrdinalIgnoreCase);

        // 3. CSRF Cookie
        var csrfCookie = cookies.FirstOrDefault(c => c.Contains(AuthCookieService.CsrfTokenCookieName));
        Assert.NotNull(csrfCookie);
        Assert.DoesNotContain("httponly", csrfCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnsGeneric401_WithoutExposingDetails()
    {
        var mockAuth = new Mock<IAuthService>();
        mockAuth.Setup(a => a.LoginAsync(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthFailureException("Internal debug reason"));

        var client = CreateClientWithAuthService(mockAuth);

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("unknown@test.com", "wrongpass", "test-tenant"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid email or password.", content);
        Assert.DoesNotContain("Internal debug reason", content);
    }

    [Fact]
    public async Task Login_RateLimited_Returns429_WithRetryAfterHeader()
    {
        var mockAuth = new Mock<IAuthService>();
        mockAuth.Setup(a => a.LoginAsync(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthRateLimitException(60));

        var client = CreateClientWithAuthService(mockAuth);

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("user@test.com", "pass", "tenant"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
        Assert.Equal("60", response.Headers.GetValues("Retry-After").First());
    }

    [Fact]
    public async Task Refresh_WithoutToken_Returns401()
    {
        var mockAuth = new Mock<IAuthService>();
        var client = CreateClientWithAuthService(mockAuth);

        var response = await client.PostAsync("/api/v1/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MutationRequest_WithAuthCookie_MissingCsrf_Returns403()
    {
        var mockAuth = new Mock<IAuthService>();
        var client = CreateClientWithAuthService(mockAuth);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("Cookie", $"{AuthCookieService.AccessTokenCookieName}=sample.token; {AuthCookieService.CsrfTokenCookieName}=csrf123");
        // Omitting X-CSRF-Token header!

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("CSRF token missing or invalid.", content);
    }
}
