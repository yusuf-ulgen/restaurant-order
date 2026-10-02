using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class RestaurantConfigRbacAndIsolationIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public RestaurantConfigRbacAndIsolationIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task EnsureMigrationsAppliedAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(RestaurantOrderDbContext).Assembly.FullName);
            })
            .Options;

        await using var context = new RestaurantOrderDbContext(options);
        await context.Database.MigrateAsync();
    }

    [Fact]
    public async Task BranchManager_CannotCreateBrandOrBranch_Returns403Forbidden()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "BM Tenant", $"bm-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var bmToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "BranchManager");

        // Attempt to create brand
        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", bmToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("BM Brand", $"bmb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        Assert.Equal(HttpStatusCode.Forbidden, brandResp.StatusCode);

        // Attempt to create branch
        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", bmToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(Guid.NewGuid(), "BM Branch", $"bmbr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        Assert.Equal(HttpStatusCode.Forbidden, branchResp.StatusCode);
    }

    [Fact]
    public async Task BranchManager_CanReadOwnBranch_ButCannotReadOtherBranch_Returns403()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "BM Scope Tenant", $"bms-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // Admin creates brand and 2 branches
        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Admin Brand", $"ab-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var br1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        br1Req.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Branch One", $"br1-{Guid.NewGuid():N}"));
        var br1Resp = await client.SendAsync(br1Req);
        var branch1 = await br1Resp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch1);

        var br2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        br2Req.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Branch Two", $"br2-{Guid.NewGuid():N}"));
        var br2Resp = await client.SendAsync(br2Req);
        var branch2 = await br2Resp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch2);

        // BranchManager assigned to Branch 1
        var bmToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branch1.Id, role: "BranchManager");

        // BM reads Branch 1 -> 200 OK
        var readOwnReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branch1.Id}", bmToken);
        var readOwnResp = await client.SendAsync(readOwnReq);
        Assert.Equal(HttpStatusCode.OK, readOwnResp.StatusCode);

        // BM reads Branch 2 -> 403 Forbidden
        var readOtherReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branch2.Id}", bmToken);
        var readOtherResp = await client.SendAsync(readOtherReq);
        Assert.Equal(HttpStatusCode.Forbidden, readOtherResp.StatusCode);

        // BM lists branches -> returns only Branch 1
        var listReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, "/api/v1/restaurant-config/branches", bmToken);
        var listResp = await client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<List<BranchDto>>();
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal(branch1.Id, list[0].Id);
    }

    [Fact]
    public async Task SuperAdmin_CannotManageTenantBrandsOrBranches_Returns403Forbidden()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var superToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: null, role: "SuperAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", superToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Super Brand", $"sb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);

        // SuperAdmin has no tenant scope or tenant.brands.manage permission -> 400 or 403 ProblemDetails
        Assert.True(brandResp.StatusCode == HttpStatusCode.Forbidden || brandResp.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TenantIsolation_TenantACannotReadOrUpdateTenantBData_Returns404()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"iso-a-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"iso-b-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var tokenA = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantA, role: "RestaurantAdmin");
        var tokenB = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantB, role: "RestaurantAdmin");

        // Tenant B creates brand and branch
        var bBrandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", tokenB);
        bBrandReq.Content = JsonContent.Create(new CreateBrandApiRequest("B Brand", $"bb-{Guid.NewGuid():N}"));
        var bBrandResp = await client.SendAsync(bBrandReq);
        var bBrand = await bBrandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(bBrand);

        var bBranchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", tokenB);
        bBranchReq.Content = JsonContent.Create(new CreateBranchApiRequest(bBrand.Id, "B Branch", $"bbr-{Guid.NewGuid():N}"));
        var bBranchResp = await client.SendAsync(bBranchReq);
        var bBranch = await bBranchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(bBranch);

        // Tenant A attempts to read Tenant B's brand -> 404
        var aGetBrandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/restaurant-config/brands/{bBrand.Id}", tokenA);
        var aGetBrandResp = await client.SendAsync(aGetBrandReq);
        Assert.Equal(HttpStatusCode.NotFound, aGetBrandResp.StatusCode);

        // Tenant A attempts to read Tenant B's branch -> 404
        var aGetBranchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/restaurant-config/branches/{bBranch.Id}", tokenA);
        var aGetBranchResp = await client.SendAsync(aGetBranchReq);
        Assert.Equal(HttpStatusCode.NotFound, aGetBranchResp.StatusCode);

        // Tenant A attempts to update Tenant B's brand -> 404
        var aPutBrandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/brands/{bBrand.Id}", tokenA);
        aPutBrandReq.Content = JsonContent.Create(new UpdateBrandApiRequest("Hacked Brand", bBrand.ConcurrencyToken));
        var aPutBrandResp = await client.SendAsync(aPutBrandReq);
        Assert.Equal(HttpStatusCode.NotFound, aPutBrandResp.StatusCode);
    }

    [Fact]
    public async Task CsrfNegative_MutationWithInvalidCsrfToken_Returns403Forbidden()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "CSRF Tenant", $"csrf-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var token = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/restaurant-config/brands");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Cookie", $"{AuthCookieService.CsrfTokenCookieName}=csrf_cookie_val");
        request.Headers.Add(AuthCookieService.CsrfHeaderName, "csrf_mismatched_val");
        request.Content = JsonContent.Create(new CreateBrandApiRequest("CSRF Brand", $"csrf-{Guid.NewGuid():N}"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Returns401ProblemDetails()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var response = await client.GetAsync("/api/v1/restaurant-config/brands");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
