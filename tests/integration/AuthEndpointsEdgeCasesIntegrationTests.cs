using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class AuthEndpointsEdgeCasesIntegrationTests : IClassFixture<WebApplicationFactory<RestaurantOrder.Api.Program>>
{
    private readonly WebApplicationFactory<RestaurantOrder.Api.Program> _factory;

    public AuthEndpointsEdgeCasesIntegrationTests(WebApplicationFactory<RestaurantOrder.Api.Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, Mock<IAuthService> MockAuth, Mock<ITokenRevocationValidator> MockValidator)
        CreateTestEnvironment()
    {
        var mockAuth = new Mock<IAuthService>();
        var mockValidator = new Mock<ITokenRevocationValidator>();

        mockValidator.Setup(v => v.ValidateTokenActiveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddScoped(_ => mockAuth.Object);
                services.AddScoped(_ => mockValidator.Object);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });

        return (client, mockAuth, mockValidator);
    }

    private string CreateTokenWithClaims(IEnumerable<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret))
        {
            KeyId = "k1"
        };
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "restaurant-order",
            audience: "restaurant-order-clients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task Login_WhenTenantSlugInHeader_UsesHeaderSlug()
    {
        var (client, mockAuth, _) = CreateTestEnvironment();
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        mockAuth.Setup(a => a.LoginAsync(
                It.Is<LoginCommand>(c => c.TenantSlug == "header-tenant"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthResult(
                "token", "refresh", now.AddMinutes(15), now.AddHours(8),
                new UserPrincipalDto(userId, "u@test.com", "Admin", Guid.NewGuid(), Guid.NewGuid(), 1),
                new SessionDto(sessionId, "Pass", "Active", now, now, now.AddHours(8), true)));

        client.DefaultRequestHeaders.Add("X-Tenant-Slug", "header-tenant");

        var request = new LoginRequest("u@test.com", "Pass123!", TenantSlug: null);
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WhenRateLimited_Returns429WithRetryAfterHeader()
    {
        var (client, mockAuth, _) = CreateTestEnvironment();
        mockAuth.Setup(a => a.LoginAsync(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthRateLimitException(retryAfterSeconds: 45, message: "Too many login attempts."));

        var request = new LoginRequest("u@test.com", "Pass123!");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("45", response.Headers.GetValues("Retry-After").FirstOrDefault());
    }

    [Fact]
    public async Task Refresh_WhenCookieMissing_Returns401()
    {
        var (client, _, _) = CreateTestEnvironment();
        var response = await client.PostAsync("/api/v1/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WhenRoleInClaimTypesRole_ResolvesSuccessfully()
    {
        var (client, _, _) = CreateTestEnvironment();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Token has ClaimTypes.Role instead of JwtClaimNames.Role
        var token = CreateTokenWithClaims(new[]
        {
            new Claim(JwtClaimNames.Subject, userId.ToString()),
            new Claim(ClaimTypes.Role, "SuperAdmin"),
            new Claim(JwtClaimNames.SessionId, sessionId.ToString()),
            new Claim(JwtClaimNames.SecurityVersion, "1"),
            new Claim(JwtClaimNames.PrincipalType, "staff"),
            new Claim(JwtClaimNames.AuthMethod, "password")
        });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SuperAdmin", body);
    }

    [Fact]
    public async Task Token_MissingRequiredClaims_FailsAuthentication()
    {
        var (client, _, _) = CreateTestEnvironment();

        // Token missing sid and sec_ver claims
        var token = CreateTokenWithClaims(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Role, "Waiter")
        });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokeSession_WhenCurrentSession_ClearsCookies()
    {
        var (client, mockAuth, _) = CreateTestEnvironment();
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();

        mockAuth.Setup(a => a.RevokeSessionAsync(new UserId(userId), currentSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var token = CreateTokenWithClaims(new[]
        {
            new Claim(JwtClaimNames.Subject, userId.ToString()),
            new Claim(JwtClaimNames.SessionId, currentSessionId.ToString()),
            new Claim(JwtClaimNames.SecurityVersion, "1"),
            new Claim(JwtClaimNames.Role, "SuperAdmin"),
            new Claim(JwtClaimNames.PrincipalType, "staff"),
            new Claim(JwtClaimNames.AuthMethod, "password")
        });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.DeleteAsync($"/api/v1/auth/sessions/{currentSessionId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.AccessTokenCookieName) && c.Contains("expires="));
    }

    [Fact]
    public async Task RevokeSession_WhenDifferentSession_DoesNotClearCookies()
    {
        var (client, mockAuth, _) = CreateTestEnvironment();
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();

        mockAuth.Setup(a => a.RevokeSessionAsync(new UserId(userId), otherSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var token = CreateTokenWithClaims(new[]
        {
            new Claim(JwtClaimNames.Subject, userId.ToString()),
            new Claim(JwtClaimNames.SessionId, currentSessionId.ToString()),
            new Claim(JwtClaimNames.SecurityVersion, "1"),
            new Claim(JwtClaimNames.Role, "SuperAdmin"),
            new Claim(JwtClaimNames.PrincipalType, "staff"),
            new Claim(JwtClaimNames.AuthMethod, "password")
        });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.DeleteAsync($"/api/v1/auth/sessions/{otherSessionId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task DistributedSecurityStateUnavailable_Returns503ProblemDetails()
    {
        var (client, _, mockValidator) = CreateTestEnvironment();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        mockValidator.Setup(v => v.ValidateTokenActiveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DistributedSecurityStateUnavailableException("Distributed session store unreachable."));

        var token = CreateTokenWithClaims(new[]
        {
            new Claim(JwtClaimNames.Subject, userId.ToString()),
            new Claim(JwtClaimNames.SessionId, sessionId.ToString()),
            new Claim(JwtClaimNames.SecurityVersion, "1"),
            new Claim(JwtClaimNames.Role, "SuperAdmin"),
            new Claim(JwtClaimNames.PrincipalType, "staff"),
            new Claim(JwtClaimNames.AuthMethod, "password")
        });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "custom-corr-123");

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Distributed session store unreachable", body);
        Assert.Contains("correlationId", body);
    }
}
