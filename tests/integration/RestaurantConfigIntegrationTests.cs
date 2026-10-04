using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class RestaurantConfigIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public RestaurantConfigIntegrationTests(TestcontainersFixture fixture)
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
    public async Task CreateBrand_ValidData_Returns201CreatedAndETag()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Tenant 1", $"t1-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var token = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var requestMsg = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            "/api/v1/restaurant-config/brands",
            token);
        requestMsg.Content = JsonContent.Create(new CreateBrandApiRequest("Burger Kingpin", $"bk-{Guid.NewGuid():N}"));

        var response = await client.SendAsync(requestMsg);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        var created = await response.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(created);
        Assert.Equal("Burger Kingpin", created.Name);
        Assert.Equal(tenantId, created.TenantId);
        Assert.Equal(BrandStatus.Active.ToString(), created.Status);
    }

    [Fact]
    public async Task CreateBrand_DuplicateSlugInSameTenant_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Tenant Dup", $"dup-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var token = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");
        var slug = $"dup-slug-{Guid.NewGuid():N}";

        var firstReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", token);
        firstReq.Content = JsonContent.Create(new CreateBrandApiRequest("First Brand", slug));
        var firstResp = await client.SendAsync(firstReq);
        Assert.Equal(HttpStatusCode.Created, firstResp.StatusCode);

        var secondReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", token);
        secondReq.Content = JsonContent.Create(new CreateBrandApiRequest("Second Brand", slug));
        var secondResp = await client.SendAsync(secondReq);

        Assert.Equal(HttpStatusCode.Conflict, secondResp.StatusCode);
    }

    [Fact]
    public async Task CreateBrand_SameSlugAcrossDifferentTenants_BothSucceed()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var tokenA = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantA, role: "RestaurantAdmin");
        var tokenB = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantB, role: "RestaurantAdmin");

        var sharedSlug = $"common-slug-{Guid.NewGuid():N}";

        var reqA = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", tokenA);
        reqA.Content = JsonContent.Create(new CreateBrandApiRequest("Brand in Tenant A", sharedSlug));
        var respA = await client.SendAsync(reqA);
        Assert.Equal(HttpStatusCode.Created, respA.StatusCode);

        var reqB = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", tokenB);
        reqB.Content = JsonContent.Create(new CreateBrandApiRequest("Brand in Tenant B", sharedSlug));
        var respB = await client.SendAsync(reqB);
        Assert.Equal(HttpStatusCode.Created, respB.StatusCode);
    }

    [Fact]
    public async Task UpdateBrand_WithValidConcurrencyToken_UpdatesAndChangesToken()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Tenant Upd", $"upd-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var token = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", token);
        createReq.Content = JsonContent.Create(new CreateBrandApiRequest("Brand Name Before", $"brand-{Guid.NewGuid():N}"));
        var createResp = await client.SendAsync(createReq);
        var brand = await createResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/brands/{brand.Id}", token);
        updateReq.Content = JsonContent.Create(new UpdateBrandApiRequest("Brand Name After", brand.ConcurrencyToken));
        var updateResp = await client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(updated);
        Assert.Equal("Brand Name After", updated.Name);
        Assert.NotEqual(brand.ConcurrencyToken, updated.ConcurrencyToken);
    }

    [Fact]
    public async Task UpdateBrand_WithStaleConcurrencyToken_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Tenant Stale", $"stale-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var token = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", token);
        createReq.Content = JsonContent.Create(new CreateBrandApiRequest("Stale Test Brand", $"stale-{Guid.NewGuid():N}"));
        var createResp = await client.SendAsync(createReq);
        var brand = await createResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/brands/{brand.Id}", token);
        updateReq.Content = JsonContent.Create(new UpdateBrandApiRequest("Brand Name New", Guid.NewGuid()));
        var updateResp = await client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.Conflict, updateResp.StatusCode);
    }

    [Fact]
    public async Task UpdateBrand_MissingConcurrencyToken_Returns412PreconditionFailed()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Tenant Pre", $"pre-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var token = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", token);
        createReq.Content = JsonContent.Create(new CreateBrandApiRequest("Pre Test Brand", $"pre-{Guid.NewGuid():N}"));
        var createResp = await client.SendAsync(createReq);
        var brand = await createResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/brands/{brand.Id}", token);
        updateReq.Content = JsonContent.Create(new UpdateBrandApiRequest("Brand Without Token"));
        var updateResp = await client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.PreconditionFailed, updateResp.StatusCode);
    }

    [Fact]
    public async Task CreateBranch_ValidData_Returns201CreatedAndETag()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Tenant Branch", $"br-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var token = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", token);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Branch Brand", $"bb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", token);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Kadikoy Branch", $"kadikoy-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branchResp = await client.SendAsync(branchReq);

        Assert.Equal(HttpStatusCode.Created, branchResp.StatusCode);
        Assert.NotNull(branchResp.Headers.ETag);

        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);
        Assert.Equal("Kadikoy Branch", branch.Name);
        Assert.Equal("Europe/Istanbul", branch.Timezone);
        Assert.Equal("TRY", branch.Currency);
        Assert.Equal(BranchStatus.Active.ToString(), branch.Status);
    }

    [Fact]
    public async Task ClosedBranch_CannotBeModified_Returns400BadRequest()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Tenant Closed", $"cl-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var token = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", token);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Closed Brand", $"cb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", token);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "To Close Branch", $"tcb-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        // Close branch
        var closeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch.Id}/close", token);
        closeReq.Content = JsonContent.Create(new BranchStateApiRequest(Reason: "Permanently closing", ConcurrencyToken: branch.ConcurrencyToken));
        var closeResp = await client.SendAsync(closeReq);
        Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);
        var closedBranch = await closeResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(closedBranch);
        Assert.Equal(BranchStatus.Closed.ToString(), closedBranch.Status);

        // Attempt to update closed branch
        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}", token);
        updateReq.Content = JsonContent.Create(new UpdateBranchApiRequest("Renamed", "Europe/Istanbul", "TRY", closedBranch.ConcurrencyToken));
        var updateResp = await client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.BadRequest, updateResp.StatusCode);

        // Attempt to activate closed branch
        var actReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch.Id}/activate", token);
        actReq.Content = JsonContent.Create(new BranchStateApiRequest(ConcurrencyToken: closedBranch.ConcurrencyToken));
        var actResp = await client.SendAsync(actReq);

        Assert.Equal(HttpStatusCode.BadRequest, actResp.StatusCode);
    }
}
