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
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class AuthEndpointsIntegrationTests : IClassFixture<WebApplicationFactory<RestaurantOrder.Api.Program>>
{
    private readonly WebApplicationFactory<RestaurantOrder.Api.Program> _factory;

    public AuthEndpointsIntegrationTests(WebApplicationFactory<RestaurantOrder.Api.Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithAuthService(
        Mock<IAuthService> mockAuthService,
        Mock<ITokenRevocationValidator>? mockValidator = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddScoped(_ => mockAuthService.Object);
                if (mockValidator != null)
                {
                    services.AddScoped(_ => mockValidator.Object);
                }
            });
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false // We inspect cookies directly
        });
    }

    private string GenerateTestToken(Guid userId, Guid sessionId, Guid? tenantId = null, string role = "RestaurantAdmin")
    {
        using var scope = _factory.Services.CreateScope();
        var jwtGen = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var authScope = tenantId.HasValue
            ? AuthorizationScope.ForTenant(TenantId.From(tenantId.Value))
            : AuthorizationScope.Platform();
        var authRole = Enum.Parse<AuthRole>(role);
        var result = jwtGen.GenerateAccessToken(
            UserId.From(userId),
            sessionId,
            PrincipalType.Staff,
            authRole,
            authScope,
            AuthenticationMethod.Password,
            1,
            DateTimeOffset.UtcNow);
        return result.Token;
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

    [Fact]
    public async Task Refresh_ValidToken_Returns200_WithNewCookies()
    {
        var mockAuth = new Mock<IAuthService>();
        var now = DateTimeOffset.UtcNow;
        var authResult = new AuthResult(
            AccessToken: "new.access.token",
            RefreshToken: "new_refresh_token_64_chars_long_opaque_value_1234567890abcdef12345678",
            AccessTokenExpiresAt: now.AddMinutes(10),
            RefreshTokenExpiresAt: now.AddDays(7),
            User: new UserPrincipalDto(Guid.NewGuid(), "admin@test.com", "RestaurantAdmin", Guid.NewGuid(), null, 1),
            Session: new SessionDto(Guid.NewGuid(), "Password", "Active", now, now, now.AddDays(7), IsCurrent: true));

        mockAuth.Setup(a => a.RefreshSessionAsync("valid_refresh_token", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        var client = CreateClientWithAuthService(mockAuth);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"{AuthCookieService.RefreshTokenCookieName}=valid_refresh_token; {AuthCookieService.CsrfTokenCookieName}=csrf_test_value");
        request.Headers.Add("X-CSRF-Token", "csrf_test_value");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.AccessTokenCookieName));
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.RefreshTokenCookieName));
    }

    [Fact]
    public async Task Refresh_TokenReuse_Returns401_AndClearsCookies()
    {
        var mockAuth = new Mock<IAuthService>();
        mockAuth.Setup(a => a.RefreshSessionAsync("stolen_token", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthFailureException("Token reuse detected"));

        var client = CreateClientWithAuthService(mockAuth);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"{AuthCookieService.RefreshTokenCookieName}=stolen_token; {AuthCookieService.CsrfTokenCookieName}=csrf_test_value");
        request.Headers.Add("X-CSRF-Token", "csrf_test_value");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.AccessTokenCookieName) && c.Contains("expires="));
    }

    [Fact]
    public async Task GetCurrentUser_WithValidToken_Returns200()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var mockAuth = new Mock<IAuthService>();
        var mockValidator = new Mock<ITokenRevocationValidator>();
        mockValidator.Setup(v => v.ValidateTokenActiveAsync(sessionId, userId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var client = CreateClientWithAuthService(mockAuth, mockValidator);
        var token = GenerateTestToken(userId, sessionId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(userId.ToString(), content);
    }

    [Fact]
    public async Task GetActiveSessions_WithValidToken_Returns200WithSessions()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var mockAuth = new Mock<IAuthService>();
        mockAuth.Setup(a => a.GetActiveSessionsAsync(new UserId(userId), sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionDto>
            {
                new SessionDto(sessionId, "Password", "Active", now, now, now.AddDays(7), IsCurrent: true)
            });

        var mockValidator = new Mock<ITokenRevocationValidator>();
        mockValidator.Setup(v => v.ValidateTokenActiveAsync(sessionId, userId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var client = CreateClientWithAuthService(mockAuth, mockValidator);
        var token = GenerateTestToken(userId, sessionId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(sessionId.ToString(), content);
    }

    [Fact]
    public async Task RevokeSession_OwnedSession_Returns204NoContent()
    {
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();

        var mockAuth = new Mock<IAuthService>();
        mockAuth.Setup(a => a.RevokeSessionAsync(new UserId(userId), targetSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockValidator = new Mock<ITokenRevocationValidator>();
        mockValidator.Setup(v => v.ValidateTokenActiveAsync(currentSessionId, userId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var client = CreateClientWithAuthService(mockAuth, mockValidator);
        var token = GenerateTestToken(userId, currentSessionId);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/auth/sessions/{targetSessionId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RevokeSession_UnownedSession_Returns404NotFound()
    {
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();

        var mockAuth = new Mock<IAuthService>();
        mockAuth.Setup(a => a.RevokeSessionAsync(new UserId(userId), targetSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // IDOR blocked / not found

        var mockValidator = new Mock<ITokenRevocationValidator>();
        mockValidator.Setup(v => v.ValidateTokenActiveAsync(currentSessionId, userId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var client = CreateClientWithAuthService(mockAuth, mockValidator);
        var token = GenerateTestToken(userId, currentSessionId);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/auth/sessions/{targetSessionId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithValidTokenAndCsrf_ClearsCookiesAndReturns200()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var mockAuth = new Mock<IAuthService>();
        var mockValidator = new Mock<ITokenRevocationValidator>();
        mockValidator.Setup(v => v.ValidateTokenActiveAsync(sessionId, userId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var client = CreateClientWithAuthService(mockAuth, mockValidator);
        var token = GenerateTestToken(userId, sessionId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Cookie", $"{AuthCookieService.CsrfTokenCookieName}=csrf123");
        request.Headers.Add("X-CSRF-Token", "csrf123");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.AccessTokenCookieName) && c.Contains("expires="));
    }

    [Fact]
    public async Task LogoutAll_WithValidTokenAndCsrf_ClearsCookiesAndReturns200()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var mockAuth = new Mock<IAuthService>();
        var mockValidator = new Mock<ITokenRevocationValidator>();
        mockValidator.Setup(v => v.ValidateTokenActiveAsync(sessionId, userId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var client = CreateClientWithAuthService(mockAuth, mockValidator);
        var token = GenerateTestToken(userId, sessionId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout-all");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Cookie", $"{AuthCookieService.CsrfTokenCookieName}=csrf123");
        request.Headers.Add("X-CSRF-Token", "csrf123");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.AccessTokenCookieName) && c.Contains("expires="));
    }
}
