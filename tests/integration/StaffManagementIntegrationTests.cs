using System.IdentityModel.Tokens.Jwt;
using System.Net;
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

public class StaffManagementIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public StaffManagementIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClient(Mock<IStaffIdentityService>? mockStaffService = null)
    {
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
                if (mockStaffService != null)
                {
                    services.AddScoped(_ => mockStaffService.Object);
                }
                var mockValidator = new Mock<ITokenRevocationValidator>();
                mockValidator.Setup(v => v.ValidateTokenActiveAsync(
                        It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                services.AddScoped(_ => mockValidator.Object);
            });
        });

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
    }

    [Fact]
    public async Task AcceptInvitation_ValidToken_Returns200()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.AcceptInvitationAsync(It.IsAny<AcceptInvitationCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var client = CreateClient(mockStaffService);

        var request = new AcceptInvitationApiRequest("valid_token_123", "NewPassword123!");
        var response = await client.PostAsJsonAsync("/api/v1/staff/accept-invitation", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_InvalidToken_Returns400()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.AcceptInvitationAsync(It.IsAny<AcceptInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid, expired, or already consumed invitation token."));

        var client = CreateClient(mockStaffService);

        var request = new AcceptInvitationApiRequest("invalid_token", "NewPassword123!");
        var response = await client.PostAsJsonAsync("/api/v1/staff/accept-invitation", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_Returns200GenericResponse()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.RequestPasswordResetAsync(It.IsAny<RequestPasswordResetCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw_reset_token");

        var client = CreateClient(mockStaffService);

        var request = new ForgotPasswordApiRequest("staff@test.com", "test-tenant");
        var response = await client.PostAsJsonAsync("/api/v1/staff/forgot-password", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("password reset link has been dispatched", content);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_Returns200()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var client = CreateClient(mockStaffService);

        var request = new ResetPasswordApiRequest("valid_reset_token", "BrandNewPassword123!");
        var response = await client.PostAsJsonAsync("/api/v1/staff/reset-password", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_InvalidToken_Returns400()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid, expired, or already consumed password reset token."));

        var client = CreateClient(mockStaffService);

        var request = new ResetPasswordApiRequest("invalid_token", "BrandNewPassword123!");
        var response = await client.PostAsJsonAsync("/api/v1/staff/reset-password", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_WhenArgumentException_Returns400()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.AcceptInvitationAsync(It.IsAny<AcceptInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Password does not meet complexity requirements."));

        var client = CreateClient(mockStaffService);

        var request = new AcceptInvitationApiRequest("token", "weak");
        var response = await client.PostAsJsonAsync("/api/v1/staff/accept-invitation", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WhenArgumentException_Returns400()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Password too short."));

        var client = CreateClient(mockStaffService);

        var request = new ResetPasswordApiRequest("token", "short");
        var response = await client.PostAsJsonAsync("/api/v1/staff/reset-password", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InviteStaff_InvalidRole_Returns400()
    {
        var client = CreateClient();
        var tenantId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new InviteStaffApiRequest("new@test.com", "NonExistentRole");
        var response = await client.PostAsJsonAsync("/api/v1/staff/invite", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InviteStaff_ValidRole_Returns200()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.InviteStaffAsync(
                It.IsAny<TenantId?>(), It.IsAny<InviteStaffCommand>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InviteStaffResult(Guid.NewGuid(), "waiter@test.com", AuthRole.Waiter, null, DateTimeOffset.UtcNow.AddHours(24)));

        var client = CreateClient(mockStaffService);
        var tenantId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new InviteStaffApiRequest("waiter@test.com", "Waiter");
        var response = await client.PostAsJsonAsync("/api/v1/staff/invite", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InviteStaff_ServiceThrowsInvalidOperationException_Returns400()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.InviteStaffAsync(
                It.IsAny<TenantId?>(), It.IsAny<InviteStaffCommand>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Email already invited."));

        var client = CreateClient(mockStaffService);
        var tenantId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new InviteStaffApiRequest("waiter@test.com", "Waiter");
        var response = await client.PostAsJsonAsync("/api/v1/staff/invite", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListStaff_WithAndWithoutBranchId_Returns200()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.ListStaffAsync(
                It.IsAny<TenantId?>(), It.IsAny<Guid?>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StaffMemberDto>());

        var client = CreateClient(mockStaffService);
        var tenantId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var resNoBranch = await client.GetAsync("/api/v1/staff");
        Assert.Equal(HttpStatusCode.OK, resNoBranch.StatusCode);

        var resWithBranch = await client.GetAsync($"/api/v1/staff?branchId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.OK, resWithBranch.StatusCode);
    }

    [Fact]
    public async Task UpdateStaffStatus_InvalidStatus_Returns400()
    {
        var client = CreateClient();
        var tenantId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new UpdateStaffStatusApiRequest("InvalidStatus");
        var response = await client.PostAsJsonAsync($"/api/v1/staff/{Guid.NewGuid()}/status", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStaffStatus_ValidStatus_Returns200()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        var client = CreateClient(mockStaffService);
        var tenantId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new UpdateStaffStatusApiRequest("Suspended");
        var response = await client.PostAsJsonAsync($"/api/v1/staff/{Guid.NewGuid()}/status", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStaffStatus_ServiceThrows_Returns400()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.UpdateStaffStatusAsync(
                It.IsAny<TenantId?>(), It.IsAny<UpdateStaffStatusCommand>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cannot suspend super admin."));

        var client = CreateClient(mockStaffService);
        var tenantId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new UpdateStaffStatusApiRequest("Suspended");
        var response = await client.PostAsJsonAsync($"/api/v1/staff/{Guid.NewGuid()}/status", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InviteStaff_WhenBranchManagerInvitesOutsideBranch_Returns403()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.InviteStaffAsync(
                It.IsAny<TenantId?>(), It.IsAny<InviteStaffCommand>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidAuthorizationScopeException("BranchManager cannot invite staff outside their assigned branch."));

        var client = CreateClient(mockStaffService);
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, branchId: branchId, role: "BranchManager");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new InviteStaffApiRequest("waiter@test.com", "Waiter", Guid.NewGuid());
        var response = await client.PostAsJsonAsync("/api/v1/staff/invite", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListStaff_WhenBranchManagerRequestsDifferentBranch_Returns403()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.ListStaffAsync(
                It.IsAny<TenantId?>(), It.IsAny<Guid?>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidAuthorizationScopeException("BranchManager cannot list staff from another branch."));

        var client = CreateClient(mockStaffService);
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, branchId: branchId, role: "BranchManager");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var response = await client.GetAsync($"/api/v1/staff?branchId={otherBranchId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStaffStatus_WhenBranchManagerUpdatesOutsideBranch_Returns403()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.UpdateStaffStatusAsync(
                It.IsAny<TenantId?>(), It.IsAny<UpdateStaffStatusCommand>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidAuthorizationScopeException("BranchManager may only update staff within their assigned branch."));

        var client = CreateClient(mockStaffService);
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, branchId: branchId, role: "BranchManager");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new UpdateStaffStatusApiRequest("Suspended");
        var response = await client.PostAsJsonAsync($"/api/v1/staff/{Guid.NewGuid()}/status", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStaffStatus_WhenMembershipNotFound_Returns404()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.UpdateStaffStatusAsync(
                It.IsAny<TenantId?>(), It.IsAny<UpdateStaffStatusCommand>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Staff membership not found."));

        var client = CreateClient(mockStaffService);
        var tenantId = Guid.NewGuid();
        var token = GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: tenantId, role: "RestaurantAdmin");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

        var request = new UpdateStaffStatusApiRequest("Suspended");
        var response = await client.PostAsJsonAsync($"/api/v1/staff/{Guid.NewGuid()}/status", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
}

