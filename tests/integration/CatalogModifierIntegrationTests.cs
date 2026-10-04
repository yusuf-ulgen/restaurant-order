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

public class CatalogModifierIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public CatalogModifierIntegrationTests(TestcontainersFixture fixture)
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
    public async Task Modifier_And_Metadata_EndToEnd_Lifecycle_And_TenantIsolation()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Mod Tenant", $"mt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Mod Branch", $"mbr-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");
        var cashierToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "Cashier");

        // 1. Create Menu, Category, and MenuItem
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuApiRequest("Lunch Menu", $"lunch-{Guid.NewGuid():N}", null, 0));
        var menuResp = await client.SendAsync(menuReq);
        var menu = await menuResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(menu);

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryApiRequest("Salads", $"salads-{Guid.NewGuid():N}", null, 0));
        var catResp = await client.SendAsync(catReq);
        var category = await catResp.Content.ReadFromJsonAsync<MenuCategoryDto>();
        Assert.NotNull(category);

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(
            CategoryId: category.Id,
            Name: "Caesar Salad",
            Slug: $"caesar-salad-{Guid.NewGuid():N}",
            BasePriceMinorUnits: 12000,
            SortOrder: 0));
        var itemResp = await client.SendAsync(itemReq);
        Assert.Equal(HttpStatusCode.Created, itemResp.StatusCode);
        var item = await itemResp.Content.ReadFromJsonAsync<MenuItemDto>();
        Assert.NotNull(item);

        // 2. Create ModifierGroup
        var groupReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/modifier-groups", adminToken);
        groupReq.Content = JsonContent.Create(new CreateModifierGroupCommand(
            Name: "Salad Dressing",
            MinSelections: 1,
            MaxSelections: 1,
            SortOrder: 0));
        var groupResp = await client.SendAsync(groupReq);
        Assert.Equal(HttpStatusCode.Created, groupResp.StatusCode);
        var group = await groupResp.Content.ReadFromJsonAsync<ModifierGroupDto>();
        Assert.NotNull(group);
        Assert.Equal("Salad Dressing", group.Name);
        Assert.Equal(1, group.MinSelections);
        Assert.Equal(1, group.MaxSelections);

        // 3. Create ModifierOptions (one free default, one paid)
        var opt1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/modifier-groups/{group.Id}/options", adminToken);
        opt1Req.Content = JsonContent.Create(new CreateModifierOptionCommand(
            Name: "Caesar Dressing",
            PriceDeltaMinorUnits: 0,
            SortOrder: 0,
            IsDefault: true));
        var opt1Resp = await client.SendAsync(opt1Req);
        Assert.Equal(HttpStatusCode.Created, opt1Resp.StatusCode);
        var opt1 = await opt1Resp.Content.ReadFromJsonAsync<ModifierOptionDto>();
        Assert.NotNull(opt1);
        Assert.True(opt1.IsDefault);
        Assert.Equal(0, opt1.PriceDeltaMinorUnits);

        var opt2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/modifier-groups/{group.Id}/options", adminToken);
        opt2Req.Content = JsonContent.Create(new CreateModifierOptionCommand(
            Name: "Blue Cheese",
            PriceDeltaMinorUnits: 150,
            SortOrder: 1,
            IsDefault: false));
        var opt2Resp = await client.SendAsync(opt2Req);
        Assert.Equal(HttpStatusCode.Created, opt2Resp.StatusCode);
        var opt2 = await opt2Resp.Content.ReadFromJsonAsync<ModifierOptionDto>();
        Assert.NotNull(opt2);
        Assert.Equal(150, opt2.PriceDeltaMinorUnits);

        // 4. Assign ModifierGroup to MenuItem
        var assignReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/modifier-groups", adminToken);
        assignReq.Headers.TryAddWithoutValidation("If-Match", $"\"{item.ConcurrencyToken:D}\"");
        assignReq.Content = JsonContent.Create(new AssignModifierGroupCommand(group.Id, SortOrder: 0));
        var assignResp = await client.SendAsync(assignReq);
        Assert.Equal(HttpStatusCode.OK, assignResp.StatusCode);
        var updatedItem = await assignResp.Content.ReadFromJsonAsync<MenuItemDto>();
        Assert.NotNull(updatedItem);
        Assert.NotNull(updatedItem.ModifierGroups);
        Assert.Single(updatedItem.ModifierGroups);
        Assert.Equal(group.Id, updatedItem.ModifierGroups[0].ModifierGroupId);

        // 5. Update Item Metadata: SpicyLevel=1, DietaryTags=["Vegetarian"], AllergenTags=["Milk"]
        var metaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/metadata", adminToken);
        metaReq.Headers.TryAddWithoutValidation("If-Match", $"\"{updatedItem.ConcurrencyToken:D}\"");
        metaReq.Content = JsonContent.Create(new UpdateMenuItemMetadataCommand(
            SpicyLevel: 1,
            DietaryTags: new[] { "Vegetarian" },
            AllergenTags: new[] { "Milk" }));
        var metaResp = await client.SendAsync(metaReq);
        Assert.Equal(HttpStatusCode.OK, metaResp.StatusCode);
        var metaItem = await metaResp.Content.ReadFromJsonAsync<MenuItemDto>();
        Assert.NotNull(metaItem);
        Assert.Equal(1, metaItem.SpicyLevel);
        Assert.Contains("Vegetarian", metaItem.DietaryTags);
        Assert.Contains("Milk", metaItem.AllergenTags);

        // 6. Contradiction Validation: Vegan + Milk allergen -> 400 Bad Request
        var badMetaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/metadata", adminToken);
        badMetaReq.Headers.TryAddWithoutValidation("If-Match", $"\"{metaItem.ConcurrencyToken:D}\"");
        badMetaReq.Content = JsonContent.Create(new UpdateMenuItemMetadataCommand(
            SpicyLevel: 1,
            DietaryTags: new[] { "Vegan" },
            AllergenTags: new[] { "Milk" }));
        var badMetaResp = await client.SendAsync(badMetaReq);
        Assert.Equal(HttpStatusCode.BadRequest, badMetaResp.StatusCode);

        // 7. Modifier Option Price Update without permission (Cashier) -> 403 Forbidden
        var unauthPriceReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/modifier-groups/{group.Id}/options/{opt2.Id}/price", cashierToken);
        unauthPriceReq.Headers.TryAddWithoutValidation("If-Match", $"\"{opt2.ConcurrencyToken:D}\"");
        unauthPriceReq.Content = JsonContent.Create(new UpdateModifierOptionPriceCommand(PriceDeltaMinorUnits: 250));
        var unauthPriceResp = await client.SendAsync(unauthPriceReq);
        Assert.Equal(HttpStatusCode.Forbidden, unauthPriceResp.StatusCode);

        // 8. Modifier Option Price Update with Admin -> 200 OK
        var authPriceReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/modifier-groups/{group.Id}/options/{opt2.Id}/price", adminToken);
        authPriceReq.Headers.TryAddWithoutValidation("If-Match", $"\"{opt2.ConcurrencyToken:D}\"");
        authPriceReq.Content = JsonContent.Create(new UpdateModifierOptionPriceCommand(PriceDeltaMinorUnits: 250));
        var authPriceResp = await client.SendAsync(authPriceReq);
        Assert.Equal(HttpStatusCode.OK, authPriceResp.StatusCode);
        var updatedOpt2 = await authPriceResp.Content.ReadFromJsonAsync<ModifierOptionDto>();
        Assert.NotNull(updatedOpt2);
        Assert.Equal(250, updatedOpt2.PriceDeltaMinorUnits);

        // 9. Cross-Tenant Isolation: Tenant 2 cannot access Tenant 1's modifier groups
        var otherTenantId = Guid.NewGuid();
        var otherAdminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), otherTenantId, role: "RestaurantAdmin");
        var crossReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/modifier-groups/{group.Id}", otherAdminToken);
        var crossResp = await client.SendAsync(crossReq);
        Assert.Equal(HttpStatusCode.Forbidden, crossResp.StatusCode);
    }

    private async Task SeedBranchAsync(Guid tenantId, Guid branchId, string name, string slug)
    {
        var brandId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        await using (var brand = conn.CreateCommand())
        {
            brand.CommandText = @"
                INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
                VALUES (@id, @tenant_id, @name, @slug, 'Active', NOW(), @token);";
            brand.Parameters.AddWithValue("id", brandId);
            brand.Parameters.AddWithValue("tenant_id", tenantId);
            brand.Parameters.AddWithValue("name", $"{name} Brand");
            brand.Parameters.AddWithValue("slug", $"brand-{slug}");
            brand.Parameters.AddWithValue("token", Guid.NewGuid());
            await brand.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.branches (
                id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token
            ) VALUES (
                @id, @tenant_id, @brand_id, @name, @slug, 'Europe/Istanbul', 'TRY', @status, NOW(), @token
            ) ON CONFLICT (id) DO NOTHING;";

        cmd.Parameters.AddWithValue("id", branchId);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("brand_id", brandId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("slug", slug);
        cmd.Parameters.AddWithValue("status", BranchStatus.Active.ToString());
        cmd.Parameters.AddWithValue("token", Guid.NewGuid());

        await cmd.ExecuteNonQueryAsync();
    }
}
