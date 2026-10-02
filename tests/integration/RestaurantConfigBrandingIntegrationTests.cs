using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class RestaurantConfigBrandingIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RestaurantConfigBrandingIntegrationTests(TestcontainersFixture fixture)
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
    public async Task GetEffectiveTheme_ReturnsDefaultTheme_WhenNoCustomAppearanceConfigured()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Brand Tenant", $"brand-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // Create Brand & Branch
        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Theme Brand", $"tb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Theme Branch", $"tbr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        // Fetch effective theme
        var themeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branch.Id}/theme", adminToken);
        var themeResp = await client.SendAsync(themeReq);
        Assert.Equal(HttpStatusCode.OK, themeResp.StatusCode);

        var theme = await themeResp.Content.ReadFromJsonAsync<EffectiveThemeDto>(JsonOptions);
        Assert.NotNull(theme);
        Assert.Equal(branch.Id, theme.BranchId);
        Assert.Equal("#ffffff", theme.SurfaceColor);
        Assert.False(theme.HasBranchOverride);
    }

    [Fact]
    public async Task UpdateBrandTheme_ValidInput_PersistsAndInheritedByBranch()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Brand Inherit Tenant", $"bit-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // 1. Create Brand & Branch
        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Inherit Brand", $"ib-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Inherit Branch", $"ibr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        // 2. Update brand theme
        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/brands/{brand.Id}/theme", adminToken);
        var updateBody = new UpdateBrandThemeApiRequest(
            DisplayName: "Custom Brand Name",
            PrimaryColor: "#112233",
            PrimaryHoverColor: "#223344",
            SecondaryColor: "#556677",
            AccentColor: "#FF5500",
            SurfaceColor: "#FAFAFA",
            BackgroundColor: "#FFFFFF",
            LogoUrl: "/assets/brand-logo.png",
            DefaultShellTitle: "Welcome to Custom Brand",
            ConcurrencyToken: Guid.NewGuid());

        updateReq.Content = JsonContent.Create(updateBody);
        var updateRes = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        var updatedBrandTheme = await updateRes.Content.ReadFromJsonAsync<BrandThemeDto>(JsonOptions);
        Assert.NotNull(updatedBrandTheme);
        Assert.Equal("Custom Brand Name", updatedBrandTheme.DisplayName);
        Assert.Equal("#112233", updatedBrandTheme.PrimaryColor);

        // 3. Verify effective branch theme inherited the brand theme
        var branchThemeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branch.Id}/theme", adminToken);
        var branchThemeRes = await client.SendAsync(branchThemeReq);
        Assert.Equal(HttpStatusCode.OK, branchThemeRes.StatusCode);

        var effectiveBranchTheme = await branchThemeRes.Content.ReadFromJsonAsync<EffectiveThemeDto>(JsonOptions);
        Assert.NotNull(effectiveBranchTheme);
        Assert.Equal("Custom Brand Name", effectiveBranchTheme.BrandDisplayName);
        Assert.Equal("Custom Brand Name", effectiveBranchTheme.BranchDisplayName);
        Assert.Equal("#112233", effectiveBranchTheme.PrimaryColor);
        Assert.Equal("/assets/brand-logo.png", effectiveBranchTheme.LogoUrl);
        Assert.False(effectiveBranchTheme.HasBranchOverride);
    }

    [Fact]
    public async Task BranchManager_CannotManageAnotherBranchOverride_ReturnsForbidden()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId1 = Guid.NewGuid();
        var branchId2 = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "BM Theme Tenant", $"bmt-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);

        // Token scoped to branchId1
        var bmToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId1, role: "BranchManager");

        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branchId2}/theme", bmToken);
        var updateBody = new UpdateBranchThemeOverrideApiRequest(
            DisplayName: "Hacked Branch",
            ConcurrencyToken: Guid.NewGuid());

        updateReq.Content = JsonContent.Create(updateBody);
        var updateRes = await client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.Forbidden, updateRes.StatusCode);
        Assert.Equal("application/problem+json", updateRes.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnsafeAssetUrl_ReturnsBadRequest()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Unsafe URL Tenant", $"uurl-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Unsafe Brand", $"ub-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/brands/{brand.Id}/theme", adminToken);
        var updateBody = new UpdateBrandThemeApiRequest(
            DisplayName: "XSS Brand",
            PrimaryColor: "#112233",
            PrimaryHoverColor: "#223344",
            SecondaryColor: "#556677",
            AccentColor: "#FF5500",
            SurfaceColor: "#FAFAFA",
            BackgroundColor: "#FFFFFF",
            LogoUrl: "javascript:alert(1)",
            ConcurrencyToken: Guid.NewGuid());

        updateReq.Content = JsonContent.Create(updateBody);
        var updateRes = await client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.BadRequest, updateRes.StatusCode);
        Assert.Equal("application/problem+json", updateRes.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task InvalidHexColor_ReturnsBadRequest()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Invalid Hex Tenant", $"ihex-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Hex Brand", $"hb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/brands/{brand.Id}/theme", adminToken);
        var updateBody = new UpdateBrandThemeApiRequest(
            DisplayName: "CSS Injection Brand",
            PrimaryColor: "red; background: url(http://attacker.com)",
            PrimaryHoverColor: "#223344",
            SecondaryColor: "#556677",
            AccentColor: "#FF5500",
            SurfaceColor: "#FAFAFA",
            BackgroundColor: "#FFFFFF",
            ConcurrencyToken: Guid.NewGuid());

        updateReq.Content = JsonContent.Create(updateBody);
        var updateRes = await client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.BadRequest, updateRes.StatusCode);
        Assert.Equal("application/problem+json", updateRes.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UpdateBrandTheme_WithValidNavigationConfig_PersistsAndInheritedByBranch()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Nav Tenant", $"nav-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Nav Brand", $"nb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Nav Branch", $"nbr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        var navJson = """[{"id":"dashboard","isVisible":true,"order":1},{"id":"menu","isVisible":true,"order":2,"labelOverride":"Gurme Menü","section":"main"}]""";
        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/brands/{brand.Id}/theme", adminToken);
        var updateBody = new UpdateBrandThemeApiRequest(
            DisplayName: "Nav Brand",
            PrimaryColor: "#112233",
            PrimaryHoverColor: "#223344",
            SecondaryColor: "#556677",
            AccentColor: "#FF5500",
            SurfaceColor: "#FAFAFA",
            BackgroundColor: "#FFFFFF",
            NavigationConfigJson: navJson,
            ConcurrencyToken: Guid.NewGuid());
        updateReq.Content = JsonContent.Create(updateBody);
        var updateRes = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // Verify effective theme contains the navigation config
        var effReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branch.Id}/theme", adminToken);
        var effRes = await client.SendAsync(effReq);
        Assert.Equal(HttpStatusCode.OK, effRes.StatusCode);
        var effective = await effRes.Content.ReadFromJsonAsync<EffectiveThemeDto>(JsonOptions);
        Assert.NotNull(effective);
        Assert.NotNull(effective.NavigationConfigJson);
        Assert.Contains("Gurme Menü", effective.NavigationConfigJson);
    }

    [Fact]
    public async Task UpdateBrandTheme_WithUnknownNavigationId_ReturnsBadRequest()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Unknown Nav Tenant", $"unav-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Unknown Nav Brand", $"unb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var unknownNavJson = """[{"id":"malicious-injected-route","isVisible":true}]""";
        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/brands/{brand.Id}/theme", adminToken);
        var updateBody = new UpdateBrandThemeApiRequest(
            DisplayName: "Unknown Nav Brand",
            PrimaryColor: "#112233",
            PrimaryHoverColor: "#223344",
            SecondaryColor: "#556677",
            AccentColor: "#FF5500",
            SurfaceColor: "#FAFAFA",
            BackgroundColor: "#FFFFFF",
            NavigationConfigJson: unknownNavJson,
            ConcurrencyToken: Guid.NewGuid());
        updateReq.Content = JsonContent.Create(updateBody);
        var updateRes = await client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.BadRequest, updateRes.StatusCode);
        Assert.Equal("application/problem+json", updateRes.Content.Headers.ContentType?.MediaType);
    }
}
