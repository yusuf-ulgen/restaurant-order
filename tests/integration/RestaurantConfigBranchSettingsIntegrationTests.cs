using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class RestaurantConfigBranchSettingsIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RestaurantConfigBranchSettingsIntegrationTests(TestcontainersFixture fixture)
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
    public async Task GetEffectiveBranchSettings_ReturnsDefaults_WhenNoCustomSettingsConfigured()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Settings Tenant", $"st-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Settings Brand", $"sb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Default Settings Branch", $"dsb-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        var getReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branch.Id}/settings", adminToken);
        var getResp = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        Assert.True(getResp.Headers.Contains("ETag"));

        var settings = await getResp.Content.ReadFromJsonAsync<EffectiveBranchSettingsDto>(JsonOptions);
        Assert.NotNull(settings);
        Assert.Equal(branch.Id, settings.BranchId);
        Assert.Equal("Europe/Istanbul", settings.Timezone);
        Assert.Equal("TRY", settings.Currency);
        Assert.Equal("tr-TR", settings.DefaultLocale);
        Assert.True(settings.PricesIncludeTax);
        Assert.Equal(1000, settings.DefaultTaxRateBps);
        Assert.False(settings.IsServiceChargeEnabled);
        Assert.Equal(0, settings.ServiceChargeRateBps);
        Assert.True(settings.IsOrderTakingEnabled);
        Assert.False(settings.HasCustomSettings);
    }

    [Fact]
    public async Task UpdateBranchSettings_ValidInput_PersistsSuccessfully()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Settings Update Tenant", $"sut-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Brand Upd", $"bup-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Branch Upd", $"brup-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/settings", adminToken);
        var updateBody = new UpdateBranchSettingsApiRequest(
            Timezone: "Europe/London",
            Currency: "GBP",
            DefaultLocale: "en-GB",
            SupportedLocales: new[] { "en-GB", "tr-TR" },
            PricesIncludeTax: true,
            DefaultTaxRateBps: 2000,
            IsServiceChargeEnabled: true,
            ServiceChargeRateBps: 1250,
            IsOrderTakingEnabled: true,
            DisplayName: "London Flagship",
            PhoneNumber: "+44 20 7946 0991",
            Email: "london@restaurant.com",
            Address: "123 Oxford St, London",
            ConcurrencyToken: branch.ConcurrencyToken);

        updateReq.Content = JsonContent.Create(updateBody);
        var updateResp = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        var updated = await updateResp.Content.ReadFromJsonAsync<BranchSettingsDto>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Europe/London", updated.Timezone);
        Assert.Equal("GBP", updated.Currency);
        Assert.Equal(2000, updated.DefaultTaxRateBps);
        Assert.True(updated.IsServiceChargeEnabled);
        Assert.Equal(1250, updated.ServiceChargeRateBps);
        Assert.Equal("London Flagship", updated.DisplayName);

        // Verify effective settings reflect changes
        var getReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branch.Id}/settings", adminToken);
        var getResp = await client.SendAsync(getReq);
        var effective = await getResp.Content.ReadFromJsonAsync<EffectiveBranchSettingsDto>(JsonOptions);
        Assert.NotNull(effective);
        Assert.True(effective.HasCustomSettings);
        Assert.Equal("Europe/London", effective.Timezone);
        Assert.Equal("GBP", effective.Currency);
    }

    [Fact]
    public async Task UpdateBranchSettings_StaleToken_ReturnsConflict409()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Conflict Tenant", $"cfl-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Conflict Brand", $"cb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Conflict Branch", $"cbr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/settings", adminToken);
        var updateBody = new UpdateBranchSettingsApiRequest(
            Timezone: "Europe/Istanbul",
            Currency: "TRY",
            DefaultLocale: "tr-TR",
            SupportedLocales: new[] { "tr-TR" },
            PricesIncludeTax: true,
            DefaultTaxRateBps: 1000,
            IsServiceChargeEnabled: false,
            ServiceChargeRateBps: 0,
            IsOrderTakingEnabled: true,
            ConcurrencyToken: Guid.NewGuid()); // Deliberately wrong token

        updateReq.Content = JsonContent.Create(updateBody);
        var updateResp = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.Conflict, updateResp.StatusCode);
    }

    [Fact]
    public async Task UpdateBranchSettings_ValidationErrors_ReturnBadRequest400()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Validation Tenant", $"val-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Val Brand", $"vb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Val Branch", $"vbr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        // Case 1: Negative tax rate
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/settings", adminToken);
        req1.Content = JsonContent.Create(new UpdateBranchSettingsApiRequest(
            "Europe/Istanbul", "TRY", "tr-TR", new[] { "tr-TR" }, true, -100, false, 0, true, ConcurrencyToken: branch.ConcurrencyToken));
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.BadRequest, resp1.StatusCode);

        // Case 2: Excessive service charge rate (> 5000 bps)
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/settings", adminToken);
        req2.Content = JsonContent.Create(new UpdateBranchSettingsApiRequest(
            "Europe/Istanbul", "TRY", "tr-TR", new[] { "tr-TR" }, true, 1000, true, 6000, true, ConcurrencyToken: branch.ConcurrencyToken));
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.BadRequest, resp2.StatusCode);

        // Case 3: Inconsistent service charge rate when disabled (> 0 bps)
        var req3 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/settings", adminToken);
        req3.Content = JsonContent.Create(new UpdateBranchSettingsApiRequest(
            "Europe/Istanbul", "TRY", "tr-TR", new[] { "tr-TR" }, true, 1000, false, 500, true, ConcurrencyToken: branch.ConcurrencyToken));
        var resp3 = await client.SendAsync(req3);
        Assert.Equal(HttpStatusCode.BadRequest, resp3.StatusCode);

        // Case 4: Invalid timezone
        var req4 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/settings", adminToken);
        req4.Content = JsonContent.Create(new UpdateBranchSettingsApiRequest(
            "Fake/Invalid_Zone", "TRY", "tr-TR", new[] { "tr-TR" }, true, 1000, false, 0, true, ConcurrencyToken: branch.ConcurrencyToken));
        var resp4 = await client.SendAsync(req4);
        Assert.Equal(HttpStatusCode.BadRequest, resp4.StatusCode);

        // Case 5: Duplicate locales
        var req5 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/settings", adminToken);
        req5.Content = JsonContent.Create(new UpdateBranchSettingsApiRequest(
            "Europe/Istanbul", "TRY", "tr-TR", new[] { "tr-TR", "tr-TR" }, true, 1000, false, 0, true, ConcurrencyToken: branch.ConcurrencyToken));
        var resp5 = await client.SendAsync(req5);
        Assert.Equal(HttpStatusCode.BadRequest, resp5.StatusCode);
    }

    [Fact]
    public async Task BranchManager_CannotManageOtherBranch_ReturnsForbidden403()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "BM Tenant", $"bmt-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("BM Brand", $"bmb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var br1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        br1Req.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Branch 1", $"br1-{Guid.NewGuid():N}"));
        var br1Resp = await client.SendAsync(br1Req);
        var br1 = await br1Resp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(br1);

        var br2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        br2Req.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Branch 2", $"br2-{Guid.NewGuid():N}"));
        var br2Resp = await client.SendAsync(br2Req);
        var br2 = await br2Resp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(br2);

        // BranchManager assigned to Branch 1
        var bmToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId: br1.Id, role: "BranchManager");

        // Can manage Branch 1
        var okReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{br1.Id}/settings", bmToken);
        okReq.Content = JsonContent.Create(new UpdateBranchSettingsApiRequest(
            "Europe/Istanbul", "TRY", "tr-TR", new[] { "tr-TR" }, true, 1000, false, 0, true, ConcurrencyToken: br1.ConcurrencyToken));
        var okResp = await client.SendAsync(okReq);
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);

        // Forbidden on Branch 2
        var forbiddenReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{br2.Id}/settings", bmToken);
        forbiddenReq.Content = JsonContent.Create(new UpdateBranchSettingsApiRequest(
            "Europe/Istanbul", "TRY", "tr-TR", new[] { "tr-TR" }, true, 1000, false, 0, true, ConcurrencyToken: br2.ConcurrencyToken));
        var forbiddenResp = await client.SendAsync(forbiddenReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);
    }

    [Fact]
    public async Task WaiterRole_CannotUpdateBranchSettings_ReturnsForbidden403()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Waiter Deny Tenant", $"wdt-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Waiter Brand", $"wb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Waiter Branch", $"wbr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId: branch.Id, role: "Waiter");

        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/settings", waiterToken);
        updateReq.Content = JsonContent.Create(new UpdateBranchSettingsApiRequest(
            "Europe/Istanbul", "TRY", "tr-TR", new[] { "tr-TR" }, true, 1000, false, 0, true, ConcurrencyToken: branch.ConcurrencyToken));
        var updateResp = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.Forbidden, updateResp.StatusCode);
    }
}
