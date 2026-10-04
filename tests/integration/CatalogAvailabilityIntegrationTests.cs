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

public class CatalogAvailabilityIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public CatalogAvailabilityIntegrationTests(TestcontainersFixture fixture)
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
    public async Task BranchItemAvailability_EndToEnd_Quick86_Restock_And_RuntimeMenuEffect()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Avail Tenant", $"at-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Avail Branch", $"abr-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // 1. Create Menu, Category, Item with 2 Variants
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("Main Menu", $"main-{Guid.NewGuid():N}"));
        var menuResp = await client.SendAsync(menuReq);
        var menu = await menuResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(menu);

        // Activate Menu
        var actMenuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/activate", adminToken);
        actMenuReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{menu.ConcurrencyToken:D}\""));
        var actMenuResp = await client.SendAsync(actMenuReq);
        Assert.Equal(HttpStatusCode.OK, actMenuResp.StatusCode);

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("Burgers", $"burgers-{Guid.NewGuid():N}"));
        var catResp = await client.SendAsync(catReq);
        var category = await catResp.Content.ReadFromJsonAsync<MenuCategoryDto>();
        Assert.NotNull(category);

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(
            CategoryId: category.Id,
            Name: "Classic Burger",
            Slug: $"classic-burger-{Guid.NewGuid():N}",
            BasePriceMinorUnits: 20000));
        var itemResp = await client.SendAsync(itemReq);
        var item = await itemResp.Content.ReadFromJsonAsync<MenuItemDto>();
        Assert.NotNull(item);

        var v1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants", adminToken);
        v1Req.Content = JsonContent.Create(new CreateItemVariantCommand("Single", "SNGL", 20000, 1, true));
        var v1Resp = await client.SendAsync(v1Req);
        var variant1 = await v1Resp.Content.ReadFromJsonAsync<ItemVariantDto>();
        Assert.NotNull(variant1);

        var v2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants", adminToken);
        v2Req.Content = JsonContent.Create(new CreateItemVariantCommand("Double", "DBL", 28000, 2, false));
        var v2Resp = await client.SendAsync(v2Req);
        var variant2 = await v2Resp.Content.ReadFromJsonAsync<ItemVariantDto>();
        Assert.NotNull(variant2);

        // 2. Check initial runtime menu: all should be available
        var initialRuntimeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", adminToken);
        var initialRuntimeResp = await client.SendAsync(initialRuntimeReq);
        Assert.Equal(HttpStatusCode.OK, initialRuntimeResp.StatusCode);
        var runtimeMenu = await initialRuntimeResp.Content.ReadFromJsonAsync<RuntimeMenuReadModel>();
        Assert.NotNull(runtimeMenu);
        Assert.Equal("TRY", runtimeMenu.Currency);
        var rItem = runtimeMenu.Menus.SelectMany(m => m.Categories).SelectMany(c => c.Items).First(i => i.Id == item.Id);
        Assert.True(rItem.IsAvailable);
        Assert.All(rItem.Variants, v => Assert.True(v.IsAvailable));

        // 3. Quick 86 item
        var q86Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/quick-86", adminToken);
        q86Req.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", "Buns run out", null, item.ConcurrencyToken));
        var q86Resp = await client.SendAsync(q86Req);
        Assert.Equal(HttpStatusCode.OK, q86Resp.StatusCode);
        Assert.NotNull(q86Resp.Headers.ETag);
        var availDto = await q86Resp.Content.ReadFromJsonAsync<BranchItemAvailabilityDto>();
        Assert.NotNull(availDto);
        Assert.False(availDto.IsAvailable);

        // 4. Runtime menu after item 86: item is unavailable AND all variants are unavailable
        var runtimeReq2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", adminToken);
        var runtimeResp2 = await client.SendAsync(runtimeReq2);
        var runtimeMenu2 = await runtimeResp2.Content.ReadFromJsonAsync<RuntimeMenuReadModel>();
        var rItem2 = runtimeMenu2!.Menus.SelectMany(m => m.Categories).SelectMany(c => c.Items).First(i => i.Id == item.Id);
        Assert.False(rItem2.IsAvailable);
        Assert.All(rItem2.Variants, v => Assert.False(v.IsAvailable));

        // 5. Restock item
        var restockReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/restock", adminToken);
        restockReq.Content = JsonContent.Create(new RestockItemCommand("Restocked buns", availDto.ConcurrencyToken));
        var restockResp = await client.SendAsync(restockReq);
        Assert.Equal(HttpStatusCode.OK, restockResp.StatusCode);
        var restockedDto = await restockResp.Content.ReadFromJsonAsync<BranchItemAvailabilityDto>();
        Assert.NotNull(restockedDto);
        Assert.True(restockedDto.IsAvailable);

        // 6. Quick 86 single variant (Double)
        var q86VarReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants/{variant2.Id}/quick-86", adminToken);
        q86VarReq.Content = JsonContent.Create(new Quick86VariantCommand("KitchenCapacity", "High grill load", null, variant2.ConcurrencyToken));
        var q86VarResp = await client.SendAsync(q86VarReq);
        Assert.Equal(HttpStatusCode.OK, q86VarResp.StatusCode);
        var varAvailDto = await q86VarResp.Content.ReadFromJsonAsync<BranchItemAvailabilityDto>();
        Assert.NotNull(varAvailDto);
        Assert.False(varAvailDto.IsAvailable);

        // 7. Runtime menu after variant 86: item is available, Single is available, Double is unavailable
        var runtimeReq3 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", adminToken);
        var runtimeResp3 = await client.SendAsync(runtimeReq3);
        var runtimeMenu3 = await runtimeResp3.Content.ReadFromJsonAsync<RuntimeMenuReadModel>();
        var rItem3 = runtimeMenu3!.Menus.SelectMany(m => m.Categories).SelectMany(c => c.Items).First(i => i.Id == item.Id);
        Assert.True(rItem3.IsAvailable);
        Assert.True(rItem3.Variants.First(v => v.Id == variant1.Id).IsAvailable);
        Assert.False(rItem3.Variants.First(v => v.Id == variant2.Id).IsAvailable);

        // 8. Restock the variant
        var restockVarReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants/{variant2.Id}/restock", adminToken);
        restockVarReq.Content = JsonContent.Create(new RestockVariantCommand("Grill capacity restored", varAvailDto.ConcurrencyToken));
        var restockVarResp = await client.SendAsync(restockVarReq);
        Assert.Equal(HttpStatusCode.OK, restockVarResp.StatusCode);
    }

    [Fact]
    public async Task Quick86_ConcurrencyTokens_EnforcesPreconditionAndConflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Conc Tenant", $"ct-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Conc Branch", $"cbr-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("Drink Menu", $"drinks-{Guid.NewGuid():N}"));
        var menuResp = await client.SendAsync(menuReq);
        var menu = await menuResp.Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("Cold Drinks", $"cold-{Guid.NewGuid():N}"));
        var catResp = await client.SendAsync(catReq);
        var category = await catResp.Content.ReadFromJsonAsync<MenuCategoryDto>();

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(
            CategoryId: category!.Id,
            Name: "Lemonade",
            Slug: $"lemonade-{Guid.NewGuid():N}",
            BasePriceMinorUnits: 8000));
        var itemResp = await client.SendAsync(itemReq);
        var item = await itemResp.Content.ReadFromJsonAsync<MenuItemDto>();

        // Missing concurrency token -> 412 Precondition Failed
        var missingTokenReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item!.Id}/quick-86", adminToken);
        missingTokenReq.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut"));
        var missingResp = await client.SendAsync(missingTokenReq);
        Assert.Equal(HttpStatusCode.PreconditionFailed, missingResp.StatusCode);

        // Stale concurrency token -> 409 Conflict
        var staleTokenReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/quick-86", adminToken);
        staleTokenReq.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: Guid.NewGuid()));
        var staleResp = await client.SendAsync(staleTokenReq);
        Assert.Equal(HttpStatusCode.Conflict, staleResp.StatusCode);
    }

    [Fact]
    public async Task Quick86_PreparationStation_Scope_Enforced_For_Kitchen_And_Bar_Roles()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Station Tenant", $"st-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Station Branch", $"sbr-{Guid.NewGuid():N}");

        var kitchenStationId = Guid.NewGuid();
        var barStationId = Guid.NewGuid();
        await SeedPreparationStationAsync(tenantId, branchId, kitchenStationId, "Main Kitchen", PreparationStationType.Kitchen);
        await SeedPreparationStationAsync(tenantId, branchId, barStationId, "Main Bar", PreparationStationType.Bar);

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");
        var kitchenToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), branchId, tenantId, role: "Kitchen");
        var barToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), branchId, tenantId, role: "Bar");
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), branchId, tenantId, role: "Waiter");

        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("Station Menu", $"sm-{Guid.NewGuid():N}"));
        var menu = await (await client.SendAsync(menuReq)).Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("Mixed Items", $"mi-{Guid.NewGuid():N}"));
        var cat = await (await client.SendAsync(catReq)).Content.ReadFromJsonAsync<MenuCategoryDto>();

        // Food item assigned to Kitchen station
        var foodReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        foodReq.Content = JsonContent.Create(new CreateMenuItemCommand(
            CategoryId: cat!.Id,
            Name: "Pizza Margherita",
            Slug: $"pizza-{Guid.NewGuid():N}",
            BasePriceMinorUnits: 15000,
            PreparationStationId: kitchenStationId));
        var food = await (await client.SendAsync(foodReq)).Content.ReadFromJsonAsync<MenuItemDto>();

        // Drink item assigned to Bar station
        var drinkReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        drinkReq.Content = JsonContent.Create(new CreateMenuItemCommand(
            CategoryId: cat.Id,
            Name: "Mojito",
            Slug: $"mojito-{Guid.NewGuid():N}",
            BasePriceMinorUnits: 12000,
            PreparationStationId: barStationId));
        var drink = await (await client.SendAsync(drinkReq)).Content.ReadFromJsonAsync<MenuItemDto>();

        // Kitchen staff closing Kitchen item -> 200 OK
        var kKitchenReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{food!.Id}/quick-86", kitchenToken);
        kKitchenReq.Content = JsonContent.Create(new Quick86ItemCommand("KitchenCapacity", ConcurrencyToken: food.ConcurrencyToken));
        var kKitchenResp = await client.SendAsync(kKitchenReq);
        Assert.Equal(HttpStatusCode.OK, kKitchenResp.StatusCode);

        // Kitchen staff attempting to close Bar item -> 403 Forbidden
        var kBarReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{drink!.Id}/quick-86", kitchenToken);
        kBarReq.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: drink.ConcurrencyToken));
        var kBarResp = await client.SendAsync(kBarReq);
        Assert.Equal(HttpStatusCode.Forbidden, kBarResp.StatusCode);

        // Bar staff closing Bar item -> 200 OK
        var bBarReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{drink.Id}/quick-86", barToken);
        bBarReq.Content = JsonContent.Create(new Quick86ItemCommand("IngredientUnavailable", ConcurrencyToken: drink.ConcurrencyToken));
        var bBarResp = await client.SendAsync(bBarReq);
        Assert.Equal(HttpStatusCode.OK, bBarResp.StatusCode);

        // Bar staff attempting to close Kitchen item -> 403 Forbidden
        var bKitchenReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{food.Id}/restock", barToken);
        bKitchenReq.Content = JsonContent.Create(new RestockItemCommand("Re-opening", food.ConcurrencyToken));
        var bKitchenResp = await client.SendAsync(bKitchenReq);
        Assert.Equal(HttpStatusCode.Forbidden, bKitchenResp.StatusCode);

        // Waiter role denied quick 86 permission -> 403 Forbidden
        var wReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{food.Id}/quick-86", waiterToken);
        wReq.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: food.ConcurrencyToken));
        var wResp = await client.SendAsync(wReq);
        Assert.Equal(HttpStatusCode.Forbidden, wResp.StatusCode);
    }

    private async Task SeedBranchAsync(Guid tenantId, Guid branchId, string name, string slug)
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
            VALUES (@id, @tenantId, @brandId, @name, @slug, 'Europe/Istanbul', 'TRY', 1, NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmd = new NpgsqlCommand(branchSql, conn))
        {
            cmd.Parameters.AddWithValue("id", branchId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("brandId", brandId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("slug", slug);
            cmd.Parameters.AddWithValue("token", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedPreparationStationAsync(Guid tenantId, Guid branchId, Guid stationId, string displayName, PreparationStationType stationType)
    {
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        var sql = @"
            INSERT INTO tenancy.preparation_stations (id, tenant_id, branch_id, display_name, station_type, is_active, sort_order, created_at, concurrency_token)
            VALUES (@id, @tenantId, @branchId, @displayName, @stationType, true, 1, NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", stationId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("displayName", displayName);
        cmd.Parameters.AddWithValue("stationType", (int)stationType);
        cmd.Parameters.AddWithValue("token", Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync();
    }
}
