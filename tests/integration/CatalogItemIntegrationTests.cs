using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class CatalogItemIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public CatalogItemIntegrationTests(TestcontainersFixture fixture)
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
    public async Task MenuItem_And_Variant_EndToEnd_Lifecycle_And_PricingPermission()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Item Tenant", $"it-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Item Branch", $"ibr-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");
        var cashierToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "Cashier");

        // 1. Create Menu and Category
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuApiRequest("Dinner Menu", $"din-{Guid.NewGuid():N}", null, 0));
        var menuResp = await client.SendAsync(menuReq);
        var menu = await menuResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(menu);

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryApiRequest("Mains", $"mains-{Guid.NewGuid():N}", null, 0));
        var catResp = await client.SendAsync(catReq);
        var category = await catResp.Content.ReadFromJsonAsync<MenuCategoryDto>();
        Assert.NotNull(category);

        // 2. Create MenuItem
        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemApiRequest(
            CategoryId: category.Id,
            Name: "Steak Burger",
            Slug: $"steak-burger-{Guid.NewGuid():N}",
            BasePriceMinorUnits: 25000,
            ShortDescription: "Prime beef patty",
            FullDescription: "Served with fries and coleslaw",
            ImageUrl: "/images/steak-burger.jpg",
            SortOrder: 1));
        var itemResp = await client.SendAsync(itemReq);
        Assert.Equal(HttpStatusCode.Created, itemResp.StatusCode);
        var item = await itemResp.Content.ReadFromJsonAsync<MenuItemDto>();
        Assert.NotNull(item);
        Assert.Equal(25000, item.BasePriceMinorUnits);
        Assert.True(item.IsActive);
        Assert.True(itemResp.Headers.Contains("ETag"));

        // 3. Update Item Price with Cashier token (lacks menu.pricing.manage -> 403 Forbidden)
        var unauthPriceReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/price", cashierToken);
        unauthPriceReq.Headers.TryAddWithoutValidation("If-Match", $"\"{item.ConcurrencyToken:D}\"");
        unauthPriceReq.Content = JsonContent.Create(new UpdateMenuItemPriceApiRequest(BasePriceMinorUnits: 28000));
        var unauthPriceResp = await client.SendAsync(unauthPriceReq);
        Assert.Equal(HttpStatusCode.Forbidden, unauthPriceResp.StatusCode);

        // 4. Update Item Price with Admin token (succeeds -> 200 OK)
        var priceReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/price", adminToken);
        priceReq.Headers.TryAddWithoutValidation("If-Match", $"\"{item.ConcurrencyToken:D}\"");
        priceReq.Content = JsonContent.Create(new UpdateMenuItemPriceApiRequest(BasePriceMinorUnits: 28000));
        var priceResp = await client.SendAsync(priceReq);
        Assert.Equal(HttpStatusCode.OK, priceResp.StatusCode);
        var updatedItem = await priceResp.Content.ReadFromJsonAsync<MenuItemDto>();
        Assert.NotNull(updatedItem);
        Assert.Equal(28000, updatedItem.BasePriceMinorUnits);

        // 5. Add Variants (Regular and Double)
        var v1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants", adminToken);
        v1Req.Content = JsonContent.Create(new CreateItemVariantApiRequest(
            Name: "Single Patty",
            Code: "SGL",
            AbsolutePriceMinorUnits: 28000,
            SortOrder: 0,
            IsDefault: true));
        var v1Resp = await client.SendAsync(v1Req);
        Assert.Equal(HttpStatusCode.Created, v1Resp.StatusCode);
        var v1 = await v1Resp.Content.ReadFromJsonAsync<ItemVariantDto>();
        Assert.NotNull(v1);
        Assert.True(v1.IsDefault);

        var v2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants", adminToken);
        v2Req.Content = JsonContent.Create(new CreateItemVariantApiRequest(
            Name: "Double Patty",
            Code: "DBL",
            AbsolutePriceMinorUnits: 36000,
            SortOrder: 1,
            IsDefault: false));
        var v2Resp = await client.SendAsync(v2Req);
        Assert.Equal(HttpStatusCode.Created, v2Resp.StatusCode);
        var v2 = await v2Resp.Content.ReadFromJsonAsync<ItemVariantDto>();
        Assert.NotNull(v2);
        Assert.False(v2.IsDefault);

        // 6. Update Variant Price with Admin
        var vPriceReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants/{v2.Id}/price", adminToken);
        vPriceReq.Headers.TryAddWithoutValidation("If-Match", $"\"{v2.ConcurrencyToken:D}\"");
        vPriceReq.Content = JsonContent.Create(new UpdateItemVariantPriceApiRequest(AbsolutePriceMinorUnits: 38000));
        var vPriceResp = await client.SendAsync(vPriceReq);
        Assert.Equal(HttpStatusCode.OK, vPriceResp.StatusCode);
        var updatedV2 = await vPriceResp.Content.ReadFromJsonAsync<ItemVariantDto>();
        Assert.NotNull(updatedV2);
        Assert.Equal(38000, updatedV2.AbsolutePriceMinorUnits);

        // 7. Missing ETag on item update returns 428 Precondition Required
        var noEtagReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}", adminToken);
        noEtagReq.Content = JsonContent.Create(new UpdateMenuItemApiRequest(
            CategoryId: category.Id,
            Name: "Steak Burger Deluxe"));
        var noEtagResp = await client.SendAsync(noEtagReq);
        Assert.Equal(HttpStatusCode.PreconditionRequired, noEtagResp.StatusCode);

        // 8. Stale ETag on item update returns 409 Conflict
        var staleEtagReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}", adminToken);
        staleEtagReq.Headers.TryAddWithoutValidation("If-Match", $"\"{Guid.NewGuid():D}\"");
        staleEtagReq.Content = JsonContent.Create(new UpdateMenuItemApiRequest(
            CategoryId: category.Id,
            Name: "Steak Burger Deluxe"));
        var staleEtagResp = await client.SendAsync(staleEtagReq);
        Assert.Equal(HttpStatusCode.Conflict, staleEtagResp.StatusCode);
    }

    [Fact]
    public async Task BranchManager_CannotModify_OtherBranchMenuItems()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchAId = Guid.NewGuid();
        var branchBId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Cross Branch Tenant", $"cb-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchAId, "Branch A", $"ba-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchBId, "Branch B", $"bb-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");
        var managerAToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchAId, role: "BranchManager");

        // Admin creates Menu on Branch B
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchBId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuApiRequest("Branch B Menu", $"b-menu-{Guid.NewGuid():N}", null, 0));
        var menuResp = await client.SendAsync(menuReq);
        var menuB = await menuResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(menuB);

        // BranchManager A attempts to create item in Branch B -> 403 Forbidden
        var crossItemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchBId}/menus/{menuB.Id}/items", managerAToken);
        crossItemReq.Content = JsonContent.Create(new CreateMenuItemApiRequest(
            CategoryId: Guid.NewGuid(),
            Name: "Unauthorized Item",
            Slug: "unauth-item",
            BasePriceMinorUnits: 1000));
        var crossItemResp = await client.SendAsync(crossItemReq);
        Assert.Equal(HttpStatusCode.Forbidden, crossItemResp.StatusCode);
    }

    private async Task SeedBranchAsync(Guid tenantId, Guid branchId, string name, string slug, BranchStatus status = BranchStatus.Active)
    {
        var brandId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        var brandSql = @"
            INSERT INTO tenancy.brands (id, tenant_id, name, slug, is_active, created_at, concurrency_token)
            VALUES (@id, @tenantId, @name, @slug, true, NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmd = new NpgsqlCommand(brandSql, conn))
        {
            cmd.Parameters.AddWithValue("id", brandId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("name", $"{name} Brand");
            cmd.Parameters.AddWithValue("slug", $"brand-{slug}");
            cmd.Parameters.AddWithValue("token", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }

        var branchSql = @"
            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
            VALUES (@id, @tenantId, @brandId, @name, @slug, 'Europe/Istanbul', 'TRY', @status, NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmd = new NpgsqlCommand(branchSql, conn))
        {
            cmd.Parameters.AddWithValue("id", branchId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("brandId", brandId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("slug", slug);
            cmd.Parameters.AddWithValue("status", (int)status);
            cmd.Parameters.AddWithValue("token", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
