using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class RbacAuthorizationIntegrationTests : IClassFixture<WebApplicationFactory<RestaurantOrder.Api.Program>>
{
    private readonly WebApplicationFactory<RestaurantOrder.Api.Program> _factory;

    public RbacAuthorizationIntegrationTests(WebApplicationFactory<RestaurantOrder.Api.Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, JwtTokenService JwtService) CreateTestClient()
    {
        JwtTokenService? jwtService = null;
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var sp = services.BuildServiceProvider();
                jwtService = sp.GetRequiredService<JwtTokenService>();
            });
        }).CreateClient();

        return (client, jwtService!);
    }

    [Fact]
    public async Task ProtectedEndpoint_WhenUnauthenticated_Returns401_WithRfc7807ProblemDetails()
    {
        var (client, _) = CreateTestClient();

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("401", content);
        Assert.Contains("Unauthorized", content);
    }

    [Fact]
    public async Task ProtectedEndpoint_WhenRoleHasPermission_Returns200()
    {
        var (client, jwtService) = CreateTestClient();

        var userId = UserId.New();
        var sessionId = Guid.NewGuid();
        var token = jwtService.GenerateAccessToken(
            userId,
            sessionId,
            PrincipalType.Staff,
            AuthRole.SuperAdmin,
            AuthorizationScope.Platform(),
            AuthenticationMethod.Password,
            1,
            DateTimeOffset.UtcNow);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(userId.Value.ToString(), content);
        Assert.Contains("SuperAdmin", content);
    }

    [Fact]
    public async Task TenantScopeTestEndpoint_ResolvesTenantFromTokenClaims()
    {
        var (client, jwtService) = CreateTestClient();

        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var token = jwtService.GenerateAccessToken(
            UserId.New(),
            Guid.NewGuid(),
            PrincipalType.Staff,
            AuthRole.Waiter,
            AuthorizationScope.ForBranch(tenantId, branchId),
            AuthenticationMethod.Password,
            1,
            DateTimeOffset.UtcNow);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/tenant-scope");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(tenantId.Value.ToString(), content);
        Assert.Contains(branchId.Value.ToString(), content);
    }

    [Fact]
    public async Task ProtectedEndpoint_WhenRoleLacksRequiredPermission_Returns403_WithRfc7807ProblemDetails()
    {
        var (client, jwtService) = CreateTestClient();

        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        // Waiter role does not possess BranchStaffManage permission
        var token = jwtService.GenerateAccessToken(
            UserId.New(),
            Guid.NewGuid(),
            PrincipalType.Staff,
            AuthRole.Waiter,
            AuthorizationScope.ForBranch(tenantId, branchId),
            AuthenticationMethod.Password,
            1,
            DateTimeOffset.UtcNow);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/staff");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("403", content);
        Assert.Contains("Forbidden", content);
    }
}
