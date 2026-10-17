using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying the atomic resolution of concurrency races
/// between staff pre-prep cancellation and kitchen preparation start,
/// as well as supervisor void authorization and audit redaction.
/// </summary>
public class StaffOrderConcurrencyIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public StaffOrderConcurrencyIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, Guid tableId, Guid sessionId, Guid menuItemId)> SetupSessionAndCatalogAsync()
    {
        var tenantId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var menuId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var menuItemId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
            VALUES (@tenantId, 'Staff Race Tenant', @tenantSlug, 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
            VALUES (@brandId, @tenantId, 'Staff Brand', @brandSlug, 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
            VALUES (@branchId, @tenantId, @brandId, 'Staff Branch', @branchSlug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.branch_settings (id, tenant_id, branch_id, timezone, currency, default_locale, supported_locales, prices_include_tax, default_tax_rate_bps, is_service_charge_enabled, service_charge_rate_bps, is_order_taking_enabled, created_at, concurrency_token)
            VALUES (gen_random_uuid(), @tenantId, @branchId, 'Europe/Istanbul', 'TRY', 'tr-TR', '[""tr-TR""]', true, 0, false, 0, true, NOW(), gen_random_uuid());

            INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
            VALUES (@areaId, @tenantId, @branchId, 'Main Hall', @areaSlug, 1, 1, true, NOW(), gen_random_uuid());

            INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, public_code, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
            VALUES (@tableId, @tenantId, @branchId, @areaId, 'T-02', 'Table 2', md5(random()::text), 4, 100, 100, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW());

            INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, activated_at_utc, created_at_utc, concurrency_token)
            VALUES (@sessionId, @tenantId, @branchId, @tableId, 2, 2, NOW(), NOW(), NOW(), gen_random_uuid());

            INSERT INTO tenancy.menus (id, tenant_id, branch_id, name, slug, status, sort_order, created_at, concurrency_token)
            VALUES (@menuId, @tenantId, @branchId, 'Staff Menu', @menuSlug, 2, 1, NOW(), gen_random_uuid());

            INSERT INTO tenancy.menu_categories (id, tenant_id, branch_id, menu_id, name, slug, sort_order, is_active, created_at, concurrency_token)
            VALUES (@categoryId, @tenantId, @branchId, @menuId, 'Main Course', @catSlug, 1, true, NOW(), gen_random_uuid());

            INSERT INTO tenancy.menu_items (id, tenant_id, branch_id, menu_id, category_id, name, slug, base_price_minor_units, sort_order, is_active, created_at, concurrency_token)
            VALUES (@menuItemId, @tenantId, @branchId, @menuId, @categoryId, 'Steak', @itemSlug, 4500, 1, true, NOW(), gen_random_uuid());
        ";

        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("tenantSlug", $"t-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("brandId", brandId);
        cmd.Parameters.AddWithValue("brandSlug", $"b-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("branchSlug", $"br-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("areaId", areaId);
        cmd.Parameters.AddWithValue("areaSlug", $"a-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("tableId", tableId);
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        cmd.Parameters.AddWithValue("menuId", menuId);
        cmd.Parameters.AddWithValue("menuSlug", $"m-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("categoryId", categoryId);
        cmd.Parameters.AddWithValue("catSlug", $"c-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("menuItemId", menuItemId);
        cmd.Parameters.AddWithValue("itemSlug", $"i-{Guid.NewGuid():N}");

        await cmd.ExecuteNonQueryAsync();
        return (tenantId, branchId, tableId, sessionId, menuItemId);
    }

    [Fact]
    public async Task StartPreparation_CommittedFirst_RejectsNormalStaffCancel()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);

        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");
        var managerToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "BranchManager");

        // 1. Waiter places staff order
        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders",
            waiterToken);
        createReq.Headers.Add("Idempotency-Key", $"staff-order-{Guid.NewGuid():N}");
        createReq.Content = JsonContent.Create(new CreateStaffOrderRequest(new List<StaffOrderItemRequest>
        {
            new(menuItemId, null, 1, "Orta pişmiş", null, 4500)
        }));

        var createRes = await client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<StaffOrderResponseDto>(JsonOptions);
        Assert.NotNull(created);
        var itemId = created.Items[0].Id;

        // 2. Kitchen transitions item to InPreparation directly via lifecycle
        var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        await using (var db = new RestaurantOrderDbContext(dbOptions))
        {
            var order = await db.Orders.IgnoreQueryFilters().Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Id));
            order.StartPreparation();
            await db.SaveChangesAsync();
        }

        // 3. Waiter (normal staff with cancel_pre_prep only) attempts pre-prep cancel
        var cancelReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{created.Id}/items/{itemId}/cancel",
            waiterToken);
        cancelReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{created.ConcurrencyToken:D}\""));
        cancelReq.Content = JsonContent.Create(new CancelOrderItemRequest("Müşteri vazgeçti"));

        var cancelRes = await client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.Conflict, cancelRes.StatusCode);

        // 4. Supervisor/Manager voids the in-prep item with supervisor authority
        // Get fresh order concurrency token
        var getReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{created.Id}",
            managerToken);
        var getRes = await client.SendAsync(getReq);
        var currentOrder = await getRes.Content.ReadFromJsonAsync<StaffOrderResponseDto>(JsonOptions);
        Assert.NotNull(currentOrder);

        var voidReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{created.Id}/items/{itemId}/cancel",
            managerToken);
        voidReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{currentOrder.ConcurrencyToken:D}\""));
        voidReq.Content = JsonContent.Create(new CancelOrderItemRequest("Zayiat olarak onaylandı"));

        var voidRes = await client.SendAsync(voidReq);
        Assert.Equal(HttpStatusCode.OK, voidRes.StatusCode);
        var voided = await voidRes.Content.ReadFromJsonAsync<StaffOrderResponseDto>(JsonOptions);
        Assert.NotNull(voided);
        Assert.Equal("Cancelled", voided.Status);
        Assert.Equal("Cancelled", voided.Items[0].Status);

        // 5. Verify audit log integrity: customer note is NOT leaked
        await using (var db = new RestaurantOrderDbContext(dbOptions))
        {
            var auditEntries = await db.OrderAuditEntries
                .Where(a => a.OrderId == new OrderId(created.Id))
                .ToListAsync();

            foreach (var audit in auditEntries)
            {
                Assert.DoesNotContain("Orta pişmiş", audit.DetailsJson);
            }
        }
    }

    [Fact]
    public async Task StaffCancel_WhenItemAlreadyCancelled_ReturnsOpenDomainError()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders",
            waiterToken);
        createReq.Headers.Add("Idempotency-Key", $"double-cancel-{Guid.NewGuid():N}");
        createReq.Content = JsonContent.Create(new CreateStaffOrderRequest(new List<StaffOrderItemRequest>
        {
            new(menuItemId, null, 1, null, null, 4500)
        }));

        var createRes = await client.SendAsync(createReq);
        var created = await createRes.Content.ReadFromJsonAsync<StaffOrderResponseDto>(JsonOptions);
        var itemId = created!.Items[0].Id;

        // First cancel succeeds
        var cancel1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{created.Id}/items/{itemId}/cancel",
            waiterToken);
        cancel1.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{created.ConcurrencyToken:D}\""));
        cancel1.Content = JsonContent.Create(new CancelOrderItemRequest("İlk iptal"));
        var res1 = await client.SendAsync(cancel1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var updated = await res1.Content.ReadFromJsonAsync<StaffOrderResponseDto>(JsonOptions);

        // Second cancel attempt on already cancelled item with updated token returns 400 Bad Request
        var cancel2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{created.Id}/items/{itemId}/cancel",
            waiterToken);
        cancel2.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{updated!.ConcurrencyToken:D}\""));
        cancel2.Content = JsonContent.Create(new CancelOrderItemRequest("İkinci iptal denemesi"));
        var res2 = await client.SendAsync(cancel2);
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
    }
}
