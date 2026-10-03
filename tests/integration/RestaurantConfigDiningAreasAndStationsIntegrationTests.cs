using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class RestaurantConfigDiningAreasAndStationsIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RestaurantConfigDiningAreasAndStationsIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, string adminToken)> SetupTenantAndBranchAsync(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Config Test Tenant", $"ct-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Test Brand", $"tb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Test Branch", $"tbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        return (tenantId, branch.Id, adminToken);
    }

    [Fact]
    public async Task DiningArea_CreateAndList_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas", adminToken);
        createReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Teras", "teras", "Terrace", 0));
        var createResp = await client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var area = await createResp.Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);
        Assert.NotNull(area);
        Assert.Equal("Teras", area.Name);
        Assert.Equal("teras", area.Code);
        Assert.Equal("Terrace", area.AreaType);
        Assert.True(area.IsActive);

        var listReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas", adminToken);
        var listResp = await client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        var areas = await listResp.Content.ReadFromJsonAsync<List<DiningAreaDto>>(JsonOptions);
        Assert.NotNull(areas);
        Assert.Single(areas);
    }

    [Fact]
    public async Task DiningArea_DuplicateCode_ReturnsConflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas", adminToken);
        req1.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Salon", "salon", "Indoor", 0));
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas", adminToken);
        req2.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Başka Salon", "salon", "Indoor", 1));
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    [Fact]
    public async Task DiningArea_DeactivateAndActivate_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas", adminToken);
        createReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Bahçe", "bahce", "Garden", 0));
        var createResp = await client.SendAsync(createReq);
        var area = await createResp.Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);
        Assert.NotNull(area);

        // Deactivate
        var deactReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas/{area.Id}/deactivate", adminToken);
        deactReq.Content = JsonContent.Create(new { ConcurrencyToken = area.ConcurrencyToken });
        var deactResp = await client.SendAsync(deactReq);
        Assert.Equal(HttpStatusCode.OK, deactResp.StatusCode);
        var deactArea = await deactResp.Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);
        Assert.NotNull(deactArea);
        Assert.False(deactArea.IsActive);

        // Activate
        var actReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas/{area.Id}/activate", adminToken);
        actReq.Content = JsonContent.Create(new { ConcurrencyToken = deactArea.ConcurrencyToken });
        var actResp = await client.SendAsync(actReq);
        Assert.Equal(HttpStatusCode.OK, actResp.StatusCode);
        var actArea = await actResp.Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);
        Assert.NotNull(actArea);
        Assert.True(actArea.IsActive);
    }

    [Fact]
    public async Task PreparationStation_CreateAndList_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/stations", adminToken);
        createReq.Content = JsonContent.Create(new CreatePreparationStationApiRequest("kitchen-main", "Ana Mutfak", "Kitchen", 0));
        var createResp = await client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var station = await createResp.Content.ReadFromJsonAsync<PreparationStationDto>(JsonOptions);
        Assert.NotNull(station);
        Assert.Equal("kitchen-main", station.Code);
        Assert.Equal("Ana Mutfak", station.DisplayName);
        Assert.Equal("Kitchen", station.StationType);
        Assert.True(station.IsActive);

        var listReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branchId}/stations", adminToken);
        var listResp = await client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        var stations = await listResp.Content.ReadFromJsonAsync<List<PreparationStationDto>>(JsonOptions);
        Assert.NotNull(stations);
        Assert.Single(stations);
    }

    [Fact]
    public async Task PreparationStation_DuplicateCode_ReturnsConflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/stations", adminToken);
        req1.Content = JsonContent.Create(new CreatePreparationStationApiRequest("bar-01", "Ana Bar", "Bar", 0));
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/stations", adminToken);
        req2.Content = JsonContent.Create(new CreatePreparationStationApiRequest("bar-01", "İkinci Bar", "Bar", 1));
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    [Fact]
    public async Task FeatureFlags_TenantDefaultsAndBranchOverrides_EffectiveEvaluated()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        // 1. Get initial effective flags (system defaults)
        var effReq1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branchId}/features/effective", adminToken);
        var effResp1 = await client.SendAsync(effReq1);
        Assert.Equal(HttpStatusCode.OK, effResp1.StatusCode);
        var eff1 = await effResp1.Content.ReadFromJsonAsync<EffectiveFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(eff1);
        Assert.False(eff1.EvaluatedFlags["Tips"]); // System default is false

        // 2. Update tenant default for Tips to true
        var tenantGetReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/v1/restaurant-config/tenant/features", adminToken);
        var tenantGetResp = await client.SendAsync(tenantGetReq);
        var tenantCurrent = await tenantGetResp.Content.ReadFromJsonAsync<TenantFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(tenantCurrent);

        var tenantUpdateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, "/api/v1/restaurant-config/tenant/features", adminToken);
        tenantUpdateReq.Content = JsonContent.Create(new UpdateFeatureFlagsApiRequest(
            new Dictionary<string, bool> { ["Tips"] = true },
            tenantCurrent.ConcurrencyToken));
        var tenantUpdateResp = await client.SendAsync(tenantUpdateReq);
        Assert.Equal(HttpStatusCode.OK, tenantUpdateResp.StatusCode);

        // 3. Check effective flags now has Tips = true from TenantDefault
        var effReq2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branchId}/features/effective", adminToken);
        var effResp2 = await client.SendAsync(effReq2);
        var eff2 = await effResp2.Content.ReadFromJsonAsync<EffectiveFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(eff2);
        Assert.True(eff2.EvaluatedFlags["Tips"]);

        // 4. Set branch override for Tips = false
        var branchGetReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branchId}/features/override", adminToken);
        var branchGetResp = await client.SendAsync(branchGetReq);
        var branchCurrent = await branchGetResp.Content.ReadFromJsonAsync<BranchFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(branchCurrent);

        var branchUpdateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branchId}/features/override", adminToken);
        branchUpdateReq.Content = JsonContent.Create(new UpdateFeatureFlagsApiRequest(
            new Dictionary<string, bool> { ["Tips"] = false },
            branchCurrent.ConcurrencyToken));
        var branchUpdateResp = await client.SendAsync(branchUpdateReq);
        Assert.Equal(HttpStatusCode.OK, branchUpdateResp.StatusCode);
        var branchUpdated = await branchUpdateResp.Content.ReadFromJsonAsync<BranchFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(branchUpdated);

        // 5. Effective should now be false due to branch override
        var effReq3 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branchId}/features/effective", adminToken);
        var effResp3 = await client.SendAsync(effReq3);
        var eff3 = await effResp3.Content.ReadFromJsonAsync<EffectiveFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(eff3);
        Assert.False(eff3.EvaluatedFlags["Tips"]);

        // 6. Clear branch override
        var clearReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Delete, $"/api/v1/restaurant-config/branches/{branchId}/features/override", adminToken);
        clearReq.Headers.TryAddWithoutValidation("If-Match", $"\"{branchUpdated.ConcurrencyToken}\"");
        var clearResp = await client.SendAsync(clearReq);
        Assert.Equal(HttpStatusCode.OK, clearResp.StatusCode);

        // 7. Effective should revert to tenant default (true)
        var effReq4 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branchId}/features/effective", adminToken);
        var effResp4 = await client.SendAsync(effReq4);
        var eff4 = await effResp4.Content.ReadFromJsonAsync<EffectiveFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(eff4);
        Assert.True(eff4.EvaluatedFlags["Tips"]);
    }

    [Fact]
    public async Task FeatureFlags_UnknownKey_ReturnsBadRequest()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branchId}/features/override", adminToken);
        req.Content = JsonContent.Create(new UpdateFeatureFlagsApiRequest(
            new Dictionary<string, bool> { ["CompletelyUnknownKey"] = true }));
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CrossTenant_Isolation_CannotAccessAnotherTenantBranch()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branch1Id, _) = await SetupTenantAndBranchAsync(client);
        var (tenant2Id, _, _) = await SetupTenantAndBranchAsync(client);

        // Tenant2 admin tries to access Tenant1's branch dining areas
        var tenant2AdminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenant2Id, role: "RestaurantAdmin");
        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branch1Id}/dining-areas", tenant2AdminToken);
        var resp = await client.SendAsync(req);

        // Tenant isolation via RLS or not found
        Assert.True(resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CrossBranch_BranchManager_ForbiddenOnOtherBranch()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branch1Id, adminToken) = await SetupTenantAndBranchAsync(client);

        // Create second branch
        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Brand2", $"b2-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branch2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branch2Req.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Branch 2", $"br2-{Guid.NewGuid():N}"));
        var branch2Resp = await client.SendAsync(branch2Req);
        var branch2 = await branch2Resp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch2);

        // Manager assigned only to branch2
        var branch2ManagerToken = RestaurantConfigTestHelpers.GenerateToken(
            Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId: branch2.Id, role: "BranchManager");

        // Tries to create dining area in branch1
        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch1Id}/dining-areas", branch2ManagerToken);
        req.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Alan", "alan", "Indoor", 0));
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
