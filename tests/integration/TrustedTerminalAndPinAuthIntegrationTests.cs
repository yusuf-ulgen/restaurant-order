using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
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

public class TrustedTerminalAndPinAuthIntegrationTests : IClassFixture<WebApplicationFactory<RestaurantOrder.Api.Program>>
{
    private readonly WebApplicationFactory<RestaurantOrder.Api.Program> _factory;

    public TrustedTerminalAndPinAuthIntegrationTests(WebApplicationFactory<RestaurantOrder.Api.Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(
        Mock<ITrustedTerminalService>? mockTerminalService = null,
        Mock<IStaffPinAuthService>? mockPinAuthService = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                if (mockTerminalService != null)
                {
                    services.AddScoped(_ => mockTerminalService.Object);
                }
                if (mockPinAuthService != null)
                {
                    services.AddScoped(_ => mockPinAuthService.Object);
                }
            });
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
    }

    [Fact]
    public async Task ActivateTerminal_ValidCode_Returns200WithCredentials()
    {
        var mockTerminalService = new Mock<ITrustedTerminalService>();
        var terminalId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        mockTerminalService.Setup(s => s.ActivateTerminalAsync(
                It.IsAny<ActivateTerminalCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivateTerminalResult(
                TerminalId: terminalId,
                DeviceSecret: "mock_secret_hex_123456",
                TenantId: tenantId,
                BranchId: branchId,
                TerminalName: "Bar Station 1"));

        var client = CreateClient(mockTerminalService: mockTerminalService);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new ActivateTerminalApiRequest(
            EnrollmentCode: "ABCD-1234",
            DeviceIdentifier: "dev-bar-01",
            TerminalName: "Bar Station 1");

        var response = await client.PostAsJsonAsync("/api/v1/terminals/activate", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ActivateTerminalApiResponse>();
        Assert.NotNull(content);
        Assert.Equal(terminalId, content.TerminalId);
        Assert.Equal(tenantId, content.TenantId);
        Assert.Equal(branchId, content.BranchId);
        Assert.Equal("Bar Station 1", content.TerminalName);

        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var terminalCookie = cookies.FirstOrDefault(c => c.Contains(AuthCookieService.TerminalCredCookieName));
        Assert.NotNull(terminalCookie);
        Assert.Contains("httponly", terminalCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActivateTerminal_InvalidCode_Returns401ProblemDetails()
    {
        var mockTerminalService = new Mock<ITrustedTerminalService>();
        mockTerminalService.Setup(s => s.ActivateTerminalAsync(
                It.IsAny<ActivateTerminalCommand>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PinAuthFailureException("Invalid or expired terminal enrollment code."));

        var client = CreateClient(mockTerminalService: mockTerminalService);

        var request = new ActivateTerminalApiRequest(
            EnrollmentCode: "EXPIRED-CODE",
            DeviceIdentifier: "dev-01",
            TerminalName: "Terminal 1");

        var response = await client.PostAsJsonAsync("/api/v1/terminals/activate", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentTerminal_WithValidHeaders_Returns200()
    {
        var mockTerminalService = new Mock<ITrustedTerminalService>();
        var terminalId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        mockTerminalService.Setup(s => s.GetCurrentTerminalAsync(
                terminalId,
                "valid_device_secret",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TerminalContext(
                TerminalId: terminalId,
                TenantId: tenantId,
                BranchId: branchId,
                TerminalName: "Kitchen Line 1",
                DeviceIdentifier: "kds-01",
                IsActive: true));

        var client = CreateClient(mockTerminalService: mockTerminalService);
        client.DefaultRequestHeaders.Add("X-Terminal-Id", terminalId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Device-Secret", "valid_device_secret");

        var response = await client.GetAsync("/api/v1/terminals/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<TerminalContext>();
        Assert.NotNull(content);
        Assert.Equal("Kitchen Line 1", content.TerminalName);
        Assert.True(content.IsActive);
    }

    [Fact]
    public async Task GetCurrentTerminal_MissingHeaders_Returns401()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/v1/terminals/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PinLogin_ValidCredentials_SetsCookiesAndReturns200()
    {
        var mockPinAuthService = new Mock<IStaffPinAuthService>();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var authResult = new AuthResult(
            AccessToken: "pin.access.token",
            RefreshToken: "pin.refresh.token.opaque",
            AccessTokenExpiresAt: now.AddMinutes(15),
            RefreshTokenExpiresAt: now.AddHours(8),
            User: new UserPrincipalDto(userId, "waiter@test.com", "Waiter", Guid.NewGuid(), Guid.NewGuid(), 1),
            Session: new SessionDto(sessionId, "Pin", "Active", now, now, now.AddHours(8), IsCurrent: true));

        mockPinAuthService.Setup(s => s.LoginWithPinAsync(
                It.IsAny<PinLoginCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        var client = CreateClient(mockPinAuthService: mockPinAuthService);

        var request = new PinLoginApiRequest(
            TerminalId: Guid.NewGuid(),
            DeviceSecret: "valid_secret",
            UserId: userId,
            Email: null,
            Pin: "1234");

        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.AccessTokenCookieName));
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.RefreshTokenCookieName));
    }

    [Fact]
    public async Task PinLogin_InvalidPin_Returns401WithGenericMessage()
    {
        var mockPinAuthService = new Mock<IStaffPinAuthService>();
        mockPinAuthService.Setup(s => s.LoginWithPinAsync(
                It.IsAny<PinLoginCommand>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PinAuthFailureException("Internal PIN check failed"));

        var client = CreateClient(mockPinAuthService: mockPinAuthService);

        var request = new PinLoginApiRequest(
            TerminalId: Guid.NewGuid(),
            DeviceSecret: "valid_secret",
            UserId: Guid.NewGuid(),
            Email: null,
            Pin: "0000");

        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PinLogin_RateLimited_Returns429WithRetryAfterHeader()
    {
        var mockPinAuthService = new Mock<IStaffPinAuthService>();
        mockPinAuthService.Setup(s => s.LoginWithPinAsync(
                It.IsAny<PinLoginCommand>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthRateLimitException(retryAfterSeconds: 60, message: "Terminal temporarily locked."));

        var client = CreateClient(mockPinAuthService: mockPinAuthService);

        var request = new PinLoginApiRequest(
            TerminalId: Guid.NewGuid(),
            DeviceSecret: "valid_secret",
            UserId: Guid.NewGuid(),
            Email: null,
            Pin: "9999");

        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/login", request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("60", response.Headers.GetValues("Retry-After").FirstOrDefault());
    }

    [Fact]
    public async Task PinLogout_ClearsCookiesAndReturns200()
    {
        var mockPinAuthService = new Mock<IStaffPinAuthService>();
        var client = CreateClient(mockPinAuthService: mockPinAuthService);

        var response = await client.PostAsync("/api/v1/auth/pin/logout", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.AccessTokenCookieName) && c.Contains("expires="));
    }

    [Fact]
    public async Task GetCurrentTerminal_WithTerminalCookie_Returns200()
    {
        var mockTerminalService = new Mock<ITrustedTerminalService>();
        var terminalId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        mockTerminalService.Setup(s => s.GetCurrentTerminalAsync(
                terminalId,
                "cookie_secret_123",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TerminalContext(
                TerminalId: terminalId,
                TenantId: tenantId,
                BranchId: branchId,
                TerminalName: "Kitchen Line 1",
                DeviceIdentifier: "kds-01",
                IsActive: true));

        var client = CreateClient(mockTerminalService: mockTerminalService);
        var rawCookie = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{terminalId:D}:cookie_secret_123"));
        client.DefaultRequestHeaders.Add("Cookie", $"{AuthCookieService.TerminalCredCookieName}={rawCookie}");

        var response = await client.GetAsync("/api/v1/terminals/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<TerminalContext>();
        Assert.NotNull(content);
        Assert.Equal("Kitchen Line 1", content.TerminalName);
    }

    [Fact]
    public async Task PinLogin_WithTerminalCookie_SetsCookiesAndReturns200()
    {
        var mockPinAuthService = new Mock<IStaffPinAuthService>();
        var terminalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var authResult = new AuthResult(
            AccessToken: "pin.access.token",
            RefreshToken: "pin.refresh.token.opaque",
            AccessTokenExpiresAt: now.AddMinutes(15),
            RefreshTokenExpiresAt: now.AddHours(8),
            User: new UserPrincipalDto(userId, "waiter@test.com", "Waiter", Guid.NewGuid(), Guid.NewGuid(), 1),
            Session: new SessionDto(sessionId, "Pin", "Active", now, now, now.AddHours(8), IsCurrent: true));

        mockPinAuthService.Setup(s => s.LoginWithPinAsync(
                It.Is<PinLoginCommand>(c => c.TerminalId == terminalId && c.DeviceSecret == "cookie_secret_123"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        var client = CreateClient(mockPinAuthService: mockPinAuthService);
        var rawCookie = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{terminalId:D}:cookie_secret_123"));
        client.DefaultRequestHeaders.Add("Cookie", $"{AuthCookieService.TerminalCredCookieName}={rawCookie}");

        var request = new PinLoginApiRequest(
            TerminalId: null,
            DeviceSecret: null,
            UserId: userId,
            Email: null,
            Pin: "1234");

        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.AccessTokenCookieName));
    }

    [Fact]
    public async Task DeactivateTerminal_ClearsTerminalAndAuthCookies()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/api/v1/terminals/deactivate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.TerminalCredCookieName) && c.Contains("expires="));
        Assert.Contains(cookies, c => c.Contains(AuthCookieService.AccessTokenCookieName) && c.Contains("expires="));
    }
}
