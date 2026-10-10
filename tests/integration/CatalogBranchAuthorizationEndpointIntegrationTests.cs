using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class CatalogBranchAuthorizationEndpointIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public CatalogBranchAuthorizationEndpointIntegrationTests(TestcontainersFixture fixture)
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

    private async Task SeedBranchAsync(Guid tenantId, Guid branchId, string name, string slug)
    {
        var brandId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        var brandSql = @"
            INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
            VALUES (@id, @tenantId, @name, @slug, 'Active', NOW(), @token)
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
            VALUES (@id, @tenantId, @brandId, @name, @slug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), @token)
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
            INSERT INTO tenancy.preparation_stations (id, tenant_id, branch_id, code, display_name, station_type, is_active, sort_order, created_at, concurrency_token)
            VALUES (@id, @tenantId, @branchId, @code, @displayName, @stationType, true, 1, NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", stationId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("code", $"PS-{stationId:N}"[..10]);
        cmd.Parameters.AddWithValue("displayName", displayName);
        cmd.Parameters.AddWithValue("stationType", (int)stationType);
        cmd.Parameters.AddWithValue("token", Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedDiningSessionAsync(Guid tenantId, Guid branchId, Guid tableSessionId)
    {
        var diningAreaId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        var sqlArea = @"
            INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
            VALUES (@areaId, @tenantId, @branchId, 'Test Area', @code, 1, 1, true, NOW(), gen_random_uuid())
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmdArea = new NpgsqlCommand(sqlArea, conn))
        {
            cmdArea.Parameters.AddWithValue("areaId", diningAreaId);
            cmdArea.Parameters.AddWithValue("tenantId", tenantId);
            cmdArea.Parameters.AddWithValue("branchId", branchId);
            cmdArea.Parameters.AddWithValue("code", $"da-{diningAreaId:N}"[..10]);
            await cmdArea.ExecuteNonQueryAsync();
        }

        var sqlTable = @"
            INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
            VALUES (@tableId, @tenantId, @branchId, @areaId, 'T-1', 'Table 1', 4, 0, 0, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW())
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmdTable = new NpgsqlCommand(sqlTable, conn))
        {
            cmdTable.Parameters.AddWithValue("tableId", tableId);
            cmdTable.Parameters.AddWithValue("tenantId", tenantId);
            cmdTable.Parameters.AddWithValue("branchId", branchId);
            cmdTable.Parameters.AddWithValue("areaId", diningAreaId);
            await cmdTable.ExecuteNonQueryAsync();
        }

        var sqlSession = @"
            INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, concurrency_token, created_at_utc)
            VALUES (@sessionId, @tenantId, @branchId, @tableId, 2, 2, NOW(), gen_random_uuid(), NOW())
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmdSession = new NpgsqlCommand(sqlSession, conn))
        {
            cmdSession.Parameters.AddWithValue("sessionId", tableSessionId);
            cmdSession.Parameters.AddWithValue("tenantId", tenantId);
            cmdSession.Parameters.AddWithValue("branchId", branchId);
            cmdSession.Parameters.AddWithValue("tableId", tableId);
            await cmdSession.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Kitchen_CanQuick86_InOwnBranch_Returns200()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Kitchen Tenant", $"kt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Kitchen Branch", $"kb-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");
        var kitchenToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "Kitchen", branchId: branchId);

        // Setup menu, category, item
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("K Menu", $"km-{Guid.NewGuid():N}"));
        var menu = await (await client.SendAsync(menuReq)).Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("K Cat", $"kc-{Guid.NewGuid():N}"));
        var cat = await (await client.SendAsync(catReq)).Content.ReadFromJsonAsync<MenuCategoryDto>();

        var stationId = Guid.NewGuid();
        await SeedPreparationStationAsync(tenantId, branchId, stationId, "Kitchen", PreparationStationType.Kitchen);

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(cat!.Id, "Soup", $"ks-{Guid.NewGuid():N}", 2000, PreparationStationId: stationId));
        var item = await (await client.SendAsync(itemReq)).Content.ReadFromJsonAsync<MenuItemDto>();

        // Kitchen Quick86 in own branch -> 200 OK
        var q86Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item!.Id}/quick-86", kitchenToken);
        q86Req.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: item.ConcurrencyToken));
        var q86Resp = await client.SendAsync(q86Req);
        Assert.Equal(HttpStatusCode.OK, q86Resp.StatusCode);
    }

    [Fact]
    public async Task BranchScopedRoles_CannotMutateOrAccess_OtherBranches_Returns403()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Cross Branch Tenant", $"cbt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchA, "Branch A", $"cba-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchB, "Branch B", $"cbb-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // Seed item in Branch B
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchB}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("B Menu", $"bm-{Guid.NewGuid():N}"));
        var menuB = await (await client.SendAsync(menuReq)).Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchB}/menus/{menuB!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("B Cat", $"bc-{Guid.NewGuid():N}"));
        var catB = await (await client.SendAsync(catReq)).Content.ReadFromJsonAsync<MenuCategoryDto>();

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchB}/menus/{menuB.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(catB!.Id, "B Steak", $"bs-{Guid.NewGuid():N}", 5000));
        var itemB = await (await client.SendAsync(itemReq)).Content.ReadFromJsonAsync<MenuItemDto>();

        // Tokens bound to Branch A
        var kitchenTokenA = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "Kitchen", branchId: branchA);
        var barTokenA = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "Bar", branchId: branchA);
        var cashierTokenA = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "Cashier", branchId: branchA);
        var waiterTokenA = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "Waiter", branchId: branchA);
        var tableSessionIdA = Guid.NewGuid();
        await SeedDiningSessionAsync(tenantId, branchA, tableSessionIdA);
        var customerTokenA = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchA, tableSessionIdA);

        // 1. Kitchen A -> Quick86 in Branch B -> 403 Forbidden
        var kReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchB}/menus/{menuB.Id}/items/{itemB!.Id}/quick-86", kitchenTokenA);
        kReq.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: itemB.ConcurrencyToken));
        var kResp = await client.SendAsync(kReq);
        Assert.Equal(HttpStatusCode.Forbidden, kResp.StatusCode);

        // 2. Bar A -> Quick86 in Branch B -> 403 Forbidden
        var bReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchB}/menus/{menuB.Id}/items/{itemB.Id}/quick-86", barTokenA);
        bReq.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: itemB.ConcurrencyToken));
        var bResp = await client.SendAsync(bReq);
        Assert.Equal(HttpStatusCode.Forbidden, bResp.StatusCode);

        // 3. Cashier A -> Quick86 in Branch B -> 403 Forbidden
        var cReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchB}/menus/{menuB.Id}/items/{itemB.Id}/quick-86", cashierTokenA);
        cReq.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: itemB.ConcurrencyToken));
        var cResp = await client.SendAsync(cReq);
        Assert.Equal(HttpStatusCode.Forbidden, cResp.StatusCode);

        // 4. Waiter A -> List Menus in Branch B -> 403 Forbidden
        var wReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchB}/menus", waiterTokenA);
        var wResp = await client.SendAsync(wReq);
        Assert.Equal(HttpStatusCode.Forbidden, wResp.StatusCode);

        // 5. Customer A -> Runtime-menu in Branch B -> 403 Forbidden
        var custReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchB}/runtime-menu", customerTokenA);
        var custResp = await client.SendAsync(custReq);
        Assert.Equal(HttpStatusCode.Forbidden, custResp.StatusCode);
    }

    [Fact]
    public async Task Customer_CanAccess_OwnBranch_RuntimeMenu_Returns200()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Cust Tenant", $"cut-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Cust Branch", $"cub-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var tableSessionId = Guid.NewGuid();
        await SeedDiningSessionAsync(tenantId, branchId, tableSessionId);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, tableSessionId);

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", customerToken);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task RestaurantAdmin_CanAccess_MultipleBranchesInSameTenant_AndBlockedCrossTenant()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var branchA1 = Guid.NewGuid();
        var branchA2 = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-adm-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-adm-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantA, branchA1, "Branch A1", $"ba1-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantA, branchA2, "Branch A2", $"ba2-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantB, branchB, "Branch B", $"bb1-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminTokenA = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantA, role: "RestaurantAdmin");

        // Access Branch A1 -> 200
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchA1}/menus", adminTokenA);
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // Access Branch A2 -> 200 (tenant-wide branch access)
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchA2}/menus", adminTokenA);
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        // Access Branch B -> 404 (isolated tenant)
        var reqB = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchB}/menus", adminTokenA);
        var respB = await client.SendAsync(reqB);
        Assert.True(respB.StatusCode == HttpStatusCode.NotFound || respB.StatusCode == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuperAdmin_PlatformScope_CannotBypass_TenantCatalog_Returns403()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "SA Tenant", $"sat-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "SA Branch", $"sab-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        // SuperAdmin token without tenant scope (platform scope)
        var superAdminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, role: "SuperAdmin");

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/menus", superAdminToken);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
