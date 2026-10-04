using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class RestaurantConfigConcurrencyIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RestaurantConfigConcurrencyIntegrationTests(TestcontainersFixture fixture)
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
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Concurrency Tenant", $"ct-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Brand Concurrency", $"bc-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        Assert.True(brandResp.IsSuccessStatusCode, $"POST /brands failed ({(int)brandResp.StatusCode}): {await brandResp.Content.ReadAsStringAsync()}");
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Branch Concurrency", $"brc-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branchResp = await client.SendAsync(branchReq);
        Assert.True(branchResp.IsSuccessStatusCode, $"POST /branches failed ({(int)branchResp.StatusCode}): {await branchResp.Content.ReadAsStringAsync()}");
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        return (tenantId, branch.Id, adminToken);
    }

    [Fact]
    public async Task FeatureFlags_FullConcurrencyFlow_SevenStepLifecycle()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        // Step 1: GET current/default state (initial when no record exists)
        var getReq1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/v1/restaurant-config/tenant/features", adminToken);
        var getResp1 = await client.SendAsync(getReq1);
        Assert.Equal(HttpStatusCode.OK, getResp1.StatusCode);

        // Step 2: Extract token / ETag (must not be Guid.Empty, parent tenant concurrency token returned)
        var flagsDto1 = await getResp1.Content.ReadFromJsonAsync<TenantFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(flagsDto1);
        Assert.NotEqual(Guid.Empty, flagsDto1.ConcurrencyToken);
        var initialToken = flagsDto1.ConcurrencyToken;

        // Step 3: PUT to create initial record using the parent token
        var putReq1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, "/api/v1/restaurant-config/tenant/features", adminToken);
        putReq1.Content = JsonContent.Create(new UpdateFeatureFlagsApiRequest(
            new Dictionary<string, bool> { ["Tips"] = true },
            initialToken));
        var putResp1 = await client.SendAsync(putReq1);
        Assert.Equal(HttpStatusCode.OK, putResp1.StatusCode);
        var flagsDto2 = await putResp1.Content.ReadFromJsonAsync<TenantFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(flagsDto2);
        Assert.NotEqual(initialToken, flagsDto2.ConcurrencyToken);
        var secondToken = flagsDto2.ConcurrencyToken;

        // Step 4: PUT update with new token
        var putReq2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, "/api/v1/restaurant-config/tenant/features", adminToken);
        putReq2.Content = JsonContent.Create(new UpdateFeatureFlagsApiRequest(
            new Dictionary<string, bool> { ["Tips"] = false },
            secondToken));
        var putResp2 = await client.SendAsync(putReq2);
        Assert.Equal(HttpStatusCode.OK, putResp2.StatusCode);
        var flagsDto3 = await putResp2.Content.ReadFromJsonAsync<TenantFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(flagsDto3);
        var thirdToken = flagsDto3.ConcurrencyToken;

        // Step 5: Retry with stale old token -> 409 Conflict
        var staleReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, "/api/v1/restaurant-config/tenant/features", adminToken);
        staleReq.Content = JsonContent.Create(new UpdateFeatureFlagsApiRequest(
            new Dictionary<string, bool> { ["Tips"] = true },
            secondToken)); // old token
        var staleResp = await client.SendAsync(staleReq);
        Assert.Equal(HttpStatusCode.Conflict, staleResp.StatusCode);

        // Step 6: Request with missing token -> 412 Precondition Failed
        var missingReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, "/api/v1/restaurant-config/tenant/features", adminToken);
        missingReq.Content = JsonContent.Create(new UpdateFeatureFlagsApiRequest(
            new Dictionary<string, bool> { ["Tips"] = true },
            null));
        var missingResp = await client.SendAsync(missingReq);
        Assert.Equal(HttpStatusCode.PreconditionFailed, missingResp.StatusCode);

        // Step 7: Verify tenant and branch isolation
        var (otherTenantId, _, otherAdminToken) = await SetupTenantAndBranchAsync(client);
        var otherGetReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/v1/restaurant-config/tenant/features", otherAdminToken);
        var otherGetResp = await client.SendAsync(otherGetReq);
        Assert.Equal(HttpStatusCode.OK, otherGetResp.StatusCode);
        var otherFlagsDto = await otherGetResp.Content.ReadFromJsonAsync<TenantFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(otherFlagsDto);
        Assert.Equal(otherTenantId, otherFlagsDto.TenantId);
        Assert.NotEqual(thirdToken, otherFlagsDto.ConcurrencyToken);
    }

    [Fact]
    public async Task DiningArea_ConcurrencyAndReorder_Validations()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        // Create 2 areas
        var c1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas", adminToken);
        c1.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Salon 1", "s1", "Indoor", 0));
        var r1 = await client.SendAsync(c1);
        var area1 = await r1.Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);
        Assert.NotNull(area1);

        var c2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas", adminToken);
        c2.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Salon 2", "s2", "Indoor", 1));
        var r2 = await client.SendAsync(c2);
        var area2 = await r2.Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);
        Assert.NotNull(area2);

        // State transition missing token -> 412
        var actMissing = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas/{area1.Id}/deactivate", adminToken);
        actMissing.Content = JsonContent.Create(new { ConcurrencyToken = (Guid?)null });
        var actMissingResp = await client.SendAsync(actMissing);
        Assert.Equal(HttpStatusCode.PreconditionFailed, actMissingResp.StatusCode);

        // State transition stale token -> 409
        var actStale = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas/{area1.Id}/deactivate", adminToken);
        actStale.Content = JsonContent.Create(new { ConcurrencyToken = Guid.NewGuid() });
        var actStaleResp = await client.SendAsync(actStale);
        Assert.Equal(HttpStatusCode.Conflict, actStaleResp.StatusCode);

        // Reorder partial list -> 400
        var reorderPartial = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas/reorder", adminToken);
        reorderPartial.Content = JsonContent.Create(new ReorderDiningAreasApiRequest(
            new List<ReorderItemApiRequest> { new(area1.Id, area1.ConcurrencyToken) }));
        var reorderPartialResp = await client.SendAsync(reorderPartial);
        Assert.Equal(HttpStatusCode.BadRequest, reorderPartialResp.StatusCode);

        // Reorder stale token -> 409
        var reorderStale = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas/reorder", adminToken);
        reorderStale.Content = JsonContent.Create(new ReorderDiningAreasApiRequest(
            new List<ReorderItemApiRequest>
            {
                new(area2.Id, Guid.NewGuid()),
                new(area1.Id, area1.ConcurrencyToken)
            }));
        var reorderStaleResp = await client.SendAsync(reorderStale);
        Assert.Equal(HttpStatusCode.Conflict, reorderStaleResp.StatusCode);

        // Reorder missing token -> 412
        var reorderMissing = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas/reorder", adminToken);
        reorderMissing.Content = JsonContent.Create(new ReorderDiningAreasApiRequest(
            new List<ReorderItemApiRequest>
            {
                new(area2.Id, (Guid?)null),
                new(area1.Id, area1.ConcurrencyToken)
            }));
        var reorderMissingResp = await client.SendAsync(reorderMissing);
        Assert.Equal(HttpStatusCode.PreconditionFailed, reorderMissingResp.StatusCode);

        // Positive reorder -> 200 OK
        var reorderOk = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/dining-areas/reorder", adminToken);
        reorderOk.Content = JsonContent.Create(new ReorderDiningAreasApiRequest(
            new List<ReorderItemApiRequest>
            {
                new(area2.Id, area2.ConcurrencyToken),
                new(area1.Id, area1.ConcurrencyToken)
            }));
        var reorderOkResp = await client.SendAsync(reorderOk);
        Assert.Equal(HttpStatusCode.OK, reorderOkResp.StatusCode);
    }

    [Fact]
    public async Task PreparationStation_ConcurrencyAndReorder_Validations()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        // Create 2 stations
        var c1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/stations", adminToken);
        c1.Content = JsonContent.Create(new CreatePreparationStationApiRequest("k1", "Kitchen 1", "Kitchen", 0));
        var r1 = await client.SendAsync(c1);
        var st1 = await r1.Content.ReadFromJsonAsync<PreparationStationDto>(JsonOptions);
        Assert.NotNull(st1);

        var c2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/stations", adminToken);
        c2.Content = JsonContent.Create(new CreatePreparationStationApiRequest("b1", "Bar 1", "Bar", 1));
        var r2 = await client.SendAsync(c2);
        var st2 = await r2.Content.ReadFromJsonAsync<PreparationStationDto>(JsonOptions);
        Assert.NotNull(st2);

        // State transition missing token -> 412
        var actMissing = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/stations/{st1.Id}/deactivate", adminToken);
        actMissing.Content = JsonContent.Create(new { ConcurrencyToken = (Guid?)null });
        var actMissingResp = await client.SendAsync(actMissing);
        Assert.Equal(HttpStatusCode.PreconditionFailed, actMissingResp.StatusCode);

        // State transition stale token -> 409
        var actStale = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/stations/{st1.Id}/deactivate", adminToken);
        actStale.Content = JsonContent.Create(new { ConcurrencyToken = Guid.NewGuid() });
        var actStaleResp = await client.SendAsync(actStale);
        Assert.Equal(HttpStatusCode.Conflict, actStaleResp.StatusCode);

        // Reorder partial list -> 400
        var reorderPartial = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/stations/reorder", adminToken);
        reorderPartial.Content = JsonContent.Create(new ReorderPreparationStationsApiRequest(
            new List<ReorderItemApiRequest> { new(st1.Id, st1.ConcurrencyToken) }));
        var reorderPartialResp = await client.SendAsync(reorderPartial);
        Assert.Equal(HttpStatusCode.BadRequest, reorderPartialResp.StatusCode);

        // Reorder stale token -> 409
        var reorderStale = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/stations/reorder", adminToken);
        reorderStale.Content = JsonContent.Create(new ReorderPreparationStationsApiRequest(
            new List<ReorderItemApiRequest>
            {
                new(st2.Id, Guid.NewGuid()),
                new(st1.Id, st1.ConcurrencyToken)
            }));
        var reorderStaleResp = await client.SendAsync(reorderStale);
        Assert.Equal(HttpStatusCode.Conflict, reorderStaleResp.StatusCode);

        // Positive reorder -> 200 OK
        var reorderOk = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branchId}/stations/reorder", adminToken);
        reorderOk.Content = JsonContent.Create(new ReorderPreparationStationsApiRequest(
            new List<ReorderItemApiRequest>
            {
                new(st2.Id, st2.ConcurrencyToken),
                new(st1.Id, st1.ConcurrencyToken)
            }));
        var reorderOkResp = await client.SendAsync(reorderOk);
        Assert.Equal(HttpStatusCode.OK, reorderOkResp.StatusCode);
    }

    [Fact]
    public async Task BranchFeatureOverride_Clear_ConcurrencyValidation()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, adminToken) = await SetupTenantAndBranchAsync(client);

        // Initial override GET to obtain token
        var getReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branchId}/features/override", adminToken);
        var getResp = await client.SendAsync(getReq);
        var current = await getResp.Content.ReadFromJsonAsync<BranchFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(current);

        // Create override
        var putReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branchId}/features/override", adminToken);
        putReq.Content = JsonContent.Create(new UpdateFeatureFlagsApiRequest(
            new Dictionary<string, bool> { ["Tips"] = true },
            current.ConcurrencyToken));
        var putResp = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        var updated = await putResp.Content.ReadFromJsonAsync<BranchFeatureFlagsDto>(JsonOptions);
        Assert.NotNull(updated);

        // Delete with missing token and no If-Match -> 412
        var delMissingReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Delete, $"/api/v1/restaurant-config/branches/{branchId}/features/override", adminToken);
        var delMissingResp = await client.SendAsync(delMissingReq);
        Assert.Equal(HttpStatusCode.PreconditionFailed, delMissingResp.StatusCode);

        // Delete with stale token -> 409
        var delStaleReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Delete, $"/api/v1/restaurant-config/branches/{branchId}/features/override", adminToken);
        delStaleReq.Headers.TryAddWithoutValidation("If-Match", $"\"{Guid.NewGuid()}\"");
        var delStaleResp = await client.SendAsync(delStaleReq);
        Assert.Equal(HttpStatusCode.Conflict, delStaleResp.StatusCode);

        // Delete with valid token -> 200 OK
        var delOkReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Delete, $"/api/v1/restaurant-config/branches/{branchId}/features/override", adminToken);
        delOkReq.Headers.TryAddWithoutValidation("If-Match", $"\"{updated.ConcurrencyToken}\"");
        var delOkResp = await client.SendAsync(delOkReq);
        Assert.Equal(HttpStatusCode.OK, delOkResp.StatusCode);
    }
}
