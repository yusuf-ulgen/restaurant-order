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

public class TrustedTerminalAndPinEnrollmentIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public TrustedTerminalAndPinEnrollmentIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private (HttpClient Client, Mock<ITrustedTerminalService> TerminalService, Mock<IStaffPinAuthService> PinService, Mock<ITokenRevocationValidator> Validator)
        CreateTestEnvironment()
    {
        var mockTerminalService = new Mock<ITrustedTerminalService>();
        var mockPinService = new Mock<IStaffPinAuthService>();
        var mockValidator = new Mock<ITokenRevocationValidator>();

        mockValidator.Setup(v => v.ValidateTokenActiveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var factory = new WebApplicationFactory<RestaurantOrder.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Database", _fixture.DatabaseConnectionString);
            builder.UseSetting("DATABASE_URL", _fixture.DatabaseConnectionString);
            builder.UseSetting("REDIS_URL", _fixture.RedisEndpoint);
            builder.UseSetting("JWT_SECRET", RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret);
            builder.UseSetting("DEPLOYMENT_COLOR", "blue");
            builder.ConfigureServices(services =>
            {
                services.AddScoped(_ => mockTerminalService.Object);
                services.AddScoped(_ => mockPinService.Object);
                services.AddScoped(_ => mockValidator.Object);
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });

        return (client, mockTerminalService, mockPinService, mockValidator);
    }

    private string GenerateToken(Guid userId, Guid sessionId, Guid? tenantId = null, Guid? branchId = null, string role = "RestaurantAdmin")
    {
        var effectiveTenantId = tenantId ?? Guid.NewGuid();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret))
        {
            KeyId = "k1"
        };
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, userId.ToString()),
            new(JwtClaimNames.SessionId, sessionId.ToString()),
            new(JwtClaimNames.JwtId, Guid.NewGuid().ToString("N")),
            new(JwtClaimNames.PrincipalType, "staff"),
            new(JwtClaimNames.Role, role),
            new(JwtClaimNames.TenantId, effectiveTenantId.ToString()),
            new(JwtClaimNames.AuthMethod, "password"),
            new(JwtClaimNames.SecurityVersion, "1"),
            new(JwtRegisteredClaimNames.Iss, "restaurant-order"),
            new(JwtRegisteredClaimNames.Aud, "restaurant-order-clients")
        };
        if (branchId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.BranchId, branchId.Value.ToString()));
        }
        var token = new JwtSecurityToken(
            issuer: "restaurant-order",
            audience: "restaurant-order-clients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task EnrollTerminal_WithNullDeviceIdentifier_GeneratesDefaultAndReturns200()
    {
        var (client, mockTerminal, _, _) = CreateTestEnvironment();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var token = GenerateToken(userId, Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");

        mockTerminal.Setup(s => s.CreateEnrollmentCodeAsync(
                It.IsAny<TenantId>(),
                It.Is<EnrollTerminalCommand>(c => c.DeviceIdentifier.StartsWith("term-")),
                It.IsAny<UserId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrollTerminalResult("ENROLL-9999", DateTimeOffset.UtcNow.AddMinutes(15), branchId, "Bar POS"));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new EnrollTerminalApiRequest(branchId, "Bar POS", null);
        var response = await client.PostAsJsonAsync("/api/v1/terminals/enroll", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RevokeTerminal_WhenTerminalFound_Returns204NoContent()
    {
        var (client, mockTerminal, _, _) = CreateTestEnvironment();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var token = GenerateToken(userId, Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");

        mockTerminal.Setup(s => s.RevokeTerminalAsync(
                It.IsAny<TenantId>(),
                terminalId,
                It.IsAny<UserId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var response = await client.PostAsync($"/api/v1/terminals/{terminalId}/revoke", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RevokeTerminal_WhenTerminalNotFound_Returns404NotFound()
    {
        var (client, mockTerminal, _, _) = CreateTestEnvironment();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var token = GenerateToken(userId, Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");

        mockTerminal.Setup(s => s.RevokeTerminalAsync(
                It.IsAny<TenantId>(),
                terminalId,
                It.IsAny<UserId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var response = await client.PostAsync($"/api/v1/terminals/{terminalId}/revoke", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentTerminal_WhenTerminalInactiveOrNull_Returns401()
    {
        var (client, mockTerminal, _, _) = CreateTestEnvironment();
        var terminalId = Guid.NewGuid();

        mockTerminal.Setup(s => s.GetCurrentTerminalAsync(
                terminalId,
                "dev_secret",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TerminalContext?)null);

        client.DefaultRequestHeaders.Add("X-Terminal-Id", terminalId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Device-Secret", "dev_secret");

        var response = await client.GetAsync("/api/v1/terminals/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PinLogin_WhenCredentialsProvidedViaCookie_Returns200()
    {
        var (client, _, mockPinService, _) = CreateTestEnvironment();
        var terminalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        mockPinService.Setup(s => s.LoginWithPinAsync(
                It.Is<PinLoginCommand>(c => c.TerminalId == terminalId && c.DeviceSecret == "cookie_secret"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthResult(
                "token", "refresh", now.AddMinutes(15), now.AddHours(8),
                new UserPrincipalDto(userId, "u@test.com", "Waiter", Guid.NewGuid(), Guid.NewGuid(), 1),
                new SessionDto(sessionId, "Pin", "Active", now, now, now.AddHours(8), true)));

        var csrfToken = "csrf_test_token_1234567890123456";
        var terminalCred = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{terminalId:D}:cookie_secret"));
        client.DefaultRequestHeaders.Add("Cookie", $"restaurant_terminal_cred={terminalCred}; restaurant_csrf_token={csrfToken}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", csrfToken);

        var request = new PinLoginApiRequest(userId, null, "1234");
        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PinLogin_WhenCredentialsProvidedViaHeadersOnly_Returns401()
    {
        var (client, _, _, _) = CreateTestEnvironment();
        var terminalId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        client.DefaultRequestHeaders.Add("X-Terminal-Id", terminalId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Device-Secret", "hdr_secret");

        var request = new PinLoginApiRequest(userId, null, "1234");
        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PinLogin_WhenTerminalCredentialsMissing_Returns401()
    {
        var (client, _, _, _) = CreateTestEnvironment();

        var request = new PinLoginApiRequest(Guid.NewGuid(), null, "1234");
        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetStaffPin_WhenSelfUser_Returns200()
    {
        var (client, _, mockPinService, _) = CreateTestEnvironment();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var token = GenerateToken(userId, Guid.NewGuid(), tenantId: tenantId, branchId: branchId, role: "Waiter");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new SetPinApiRequest(userId, branchId, "4321");
        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/set", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetStaffPin_WhenDifferentUserWithoutPermission_Returns403()
    {
        var (client, _, _, _) = CreateTestEnvironment();
        var actorUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var token = GenerateToken(actorUserId, Guid.NewGuid(), tenantId: tenantId, branchId: branchId, role: "Waiter");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new SetPinApiRequest(targetUserId, branchId, "4321");
        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/set", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetStaffPin_WhenInvalidPin_Returns400()
    {
        var (client, _, mockPinService, _) = CreateTestEnvironment();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var token = GenerateToken(userId, Guid.NewGuid(), tenantId: tenantId, branchId: branchId, role: "Waiter");

        mockPinService.Setup(s => s.SetPinAsync(
                It.IsAny<TenantId>(), It.IsAny<SetStaffPinCommand>(), It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("PIN must be exactly 4 digits."));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new SetPinApiRequest(userId, branchId, "12");
        var response = await client.PostAsJsonAsync("/api/v1/auth/pin/set", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
