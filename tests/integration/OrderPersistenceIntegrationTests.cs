using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Verifies PostgreSQL database persistence for Orders, OrderItems, OrderItemModifiers,
/// and OrderAuditEntries including RLS tenant isolation, composite unique constraints,
/// and immutable audit triggers.
/// </summary>
public class OrderPersistenceIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public OrderPersistenceIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<string> GetRuntimeConnectionStringAsync()
    {
        await EnsureMigrationsAppliedAsync();
        return await _fixture.ProvisionTemporaryRuntimeRoleAsync();
    }

    private async Task<(Guid tenantId, Guid branchId, Guid tableId, Guid sessionId, Guid menuItemId)> SeedBaseHierarchyAsync(NpgsqlConnection conn)
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

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
            VALUES (@tenantId, 'Order Test Tenant', @tenantSlug, 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
            VALUES (@brandId, @tenantId, 'Order Brand', @brandSlug, 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, created_at, concurrency_token)
            VALUES (@branchId, @tenantId, @brandId, 'Order Branch', @branchSlug, 'Europe/Istanbul', 'TRY', NOW(), gen_random_uuid());

            INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, slug, area_type, sort_order, created_at, concurrency_token)
            VALUES (@areaId, @tenantId, @branchId, 'Main Hall', @areaSlug, 'Indoor', 1, NOW(), gen_random_uuid());

            INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, capacity, is_active, public_code, created_at, concurrency_token)
            VALUES (@tableId, @tenantId, @branchId, @areaId, 'T-01', 4, true, md5(random()::text), NOW(), gen_random_uuid());

            INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, activated_at_utc, created_at_utc, concurrency_token)
            VALUES (@sessionId, @tenantId, @branchId, @tableId, 2, 2, NOW(), NOW(), NOW(), gen_random_uuid());

            INSERT INTO tenancy.menus (id, tenant_id, branch_id, name, slug, status, sort_order, created_at, concurrency_token)
            VALUES (@menuId, @tenantId, @branchId, 'Main Menu', @menuSlug, 1, 1, NOW(), gen_random_uuid());

            INSERT INTO tenancy.menu_categories (id, tenant_id, branch_id, menu_id, name, slug, sort_order, is_active, created_at, concurrency_token)
            VALUES (@categoryId, @tenantId, @branchId, @menuId, 'Burgers', @catSlug, 1, true, NOW(), gen_random_uuid());

            INSERT INTO tenancy.menu_items (id, tenant_id, branch_id, menu_id, category_id, name, slug, base_price_minor_units, sort_order, is_active, created_at, concurrency_token)
            VALUES (@menuItemId, @tenantId, @branchId, @menuId, @categoryId, 'Classic Burger', @itemSlug, 2500, 1, true, NOW(), gen_random_uuid());
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
    public async Task Migration_AppliesSuccessfully()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();
    }

    [Fact]
    public async Task UniqueConstraint_DuplicateRoundNumberInSameSession_ThrowsDbUpdateException()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        await using var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await ownerConn.OpenAsync();
        var (tenantId, branchId, tableId, sessionId, menuItemId) = await SeedBaseHierarchyAsync(ownerConn);

        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;

        var tenantContext = new TenantContext(tenantId);
        await using var context = new RestaurantOrderDbContext(options, tenantContext);

        var item1 = OrderItem.Create(
            OrderId.New(),
            new TenantId(tenantId),
            new MenuItemId(menuItemId),
            null,
            "Burger",
            null,
            1,
            2500L,
            1000);

        var order1 = Order.CreateSubmitted(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            new RestaurantTableId(tableId),
            roundNumber: 1,
            OrderSource.Customer,
            Currency.Default,
            idempotencyKey: $"key-{Guid.NewGuid():N}",
            new[] { item1 });

        context.Orders.Add(order1);
        await context.SaveChangesAsync();

        var item2 = OrderItem.Create(
            OrderId.New(),
            new TenantId(tenantId),
            new MenuItemId(menuItemId),
            null,
            "Burger",
            null,
            1,
            2500L,
            1000);

        // Same session, same round number 1
        var duplicateOrder = Order.CreateSubmitted(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            new RestaurantTableId(tableId),
            roundNumber: 1,
            OrderSource.Customer,
            Currency.Default,
            idempotencyKey: $"key-{Guid.NewGuid():N}",
            new[] { item2 });

        context.Orders.Add(duplicateOrder);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task UniqueConstraint_DuplicateIdempotencyKey_ThrowsDbUpdateException()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        await using var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await ownerConn.OpenAsync();
        var (tenantId, branchId, tableId, sessionId, menuItemId) = await SeedBaseHierarchyAsync(ownerConn);

        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;

        var tenantContext = new TenantContext(tenantId);
        await using var context = new RestaurantOrderDbContext(options, tenantContext);

        var sharedKey = $"shared-key-{Guid.NewGuid():N}";

        var item1 = OrderItem.Create(
            OrderId.New(),
            new TenantId(tenantId),
            new MenuItemId(menuItemId),
            null,
            "Burger",
            null,
            1,
            2500L,
            1000);

        var order1 = Order.CreateSubmitted(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            new RestaurantTableId(tableId),
            roundNumber: 1,
            OrderSource.Customer,
            Currency.Default,
            idempotencyKey: sharedKey,
            new[] { item1 });

        context.Orders.Add(order1);
        await context.SaveChangesAsync();

        var item2 = OrderItem.Create(
            OrderId.New(),
            new TenantId(tenantId),
            new MenuItemId(menuItemId),
            null,
            "Burger",
            null,
            1,
            2500L,
            1000);

        // Different round, same idempotency key
        var order2 = Order.CreateSubmitted(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            new RestaurantTableId(tableId),
            roundNumber: 2,
            OrderSource.Customer,
            Currency.Default,
            idempotencyKey: sharedKey,
            new[] { item2 });

        context.Orders.Add(order2);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ImmutableAuditTrigger_BlocksUpdateAndDeleteOnOrderAuditEntries()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        await using var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await ownerConn.OpenAsync();
        var (tenantId, branchId, tableId, sessionId, menuItemId) = await SeedBaseHierarchyAsync(ownerConn);

        var orderId = Guid.NewGuid();
        var auditId = Guid.NewGuid();

        await using var cmd = ownerConn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.orders (id, tenant_id, branch_id, dining_session_id, table_id, round_number, order_source, currency, status, subtotal_minor_units, tax_minor_units, service_charge_minor_units, total_minor_units, idempotency_key, request_fingerprint, concurrency_token, created_at_utc)
            VALUES (@orderId, @tenantId, @branchId, @sessionId, @tableId, 1, 1, 'TRY', 2, 2500, 250, 0, 2750, @key, 'fp', gen_random_uuid(), NOW());

            INSERT INTO tenancy.order_audit_entries (id, tenant_id, order_id, dining_session_id, actor_id, actor_role, action, new_status, created_at_utc)
            VALUES (@auditId, @tenantId, @orderId, @sessionId, gen_random_uuid(), 'Customer', 'OrderSubmitted', 2, NOW());
        ";
        cmd.Parameters.AddWithValue("orderId", orderId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        cmd.Parameters.AddWithValue("tableId", tableId);
        cmd.Parameters.AddWithValue("key", $"audit-test-key-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("auditId", auditId);

        await cmd.ExecuteNonQueryAsync();

        // 1. Attempt UPDATE on immutable audit entry
        await using var updateCmd = ownerConn.CreateCommand();
        updateCmd.CommandText = "UPDATE tenancy.order_audit_entries SET action = 'Hacked' WHERE id = @auditId;";
        updateCmd.Parameters.AddWithValue("auditId", auditId);

        var updateEx = await Assert.ThrowsAsync<PostgresException>(() => updateCmd.ExecuteNonQueryAsync());
        Assert.Contains("strictly immutable", updateEx.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Attempt DELETE on immutable audit entry
        await using var deleteCmd = ownerConn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM tenancy.order_audit_entries WHERE id = @auditId;";
        deleteCmd.Parameters.AddWithValue("auditId", auditId);

        var deleteEx = await Assert.ThrowsAsync<PostgresException>(() => deleteCmd.ExecuteNonQueryAsync());
        Assert.Contains("strictly immutable", deleteEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostgreSqlRls_TenantIsolation_EnforcedForOrders()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        var runtimeConnStr = await GetRuntimeConnectionStringAsync();

        await using var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await ownerConn.OpenAsync();

        var (tenantAId, branchAId, tableAId, sessionAId, _) = await SeedBaseHierarchyAsync(ownerConn);
        var (tenantBId, branchBId, tableBId, sessionBId, _) = await SeedBaseHierarchyAsync(ownerConn);

        var orderAId = Guid.NewGuid();
        var orderBId = Guid.NewGuid();

        await using var seedCmd = ownerConn.CreateCommand();
        seedCmd.CommandText = @"
            INSERT INTO tenancy.orders (id, tenant_id, branch_id, dining_session_id, table_id, round_number, order_source, currency, status, subtotal_minor_units, tax_minor_units, service_charge_minor_units, total_minor_units, idempotency_key, request_fingerprint, concurrency_token, created_at_utc)
            VALUES (@orderAId, @tenantAId, @branchAId, @sessionAId, @tableAId, 1, 1, 'TRY', 2, 1000, 100, 0, 1100, @keyA, 'fpA', gen_random_uuid(), NOW());

            INSERT INTO tenancy.orders (id, tenant_id, branch_id, dining_session_id, table_id, round_number, order_source, currency, status, subtotal_minor_units, tax_minor_units, service_charge_minor_units, total_minor_units, idempotency_key, request_fingerprint, concurrency_token, created_at_utc)
            VALUES (@orderBId, @tenantBId, @branchBId, @sessionBId, @tableBId, 1, 1, 'TRY', 2, 2000, 200, 0, 2200, @keyB, 'fpB', gen_random_uuid(), NOW());
        ";
        seedCmd.Parameters.AddWithValue("orderAId", orderAId);
        seedCmd.Parameters.AddWithValue("tenantAId", tenantAId);
        seedCmd.Parameters.AddWithValue("branchAId", branchAId);
        seedCmd.Parameters.AddWithValue("sessionAId", sessionAId);
        seedCmd.Parameters.AddWithValue("tableAId", tableAId);
        seedCmd.Parameters.AddWithValue("keyA", $"key-A-{Guid.NewGuid():N}");

        seedCmd.Parameters.AddWithValue("orderBId", orderBId);
        seedCmd.Parameters.AddWithValue("tenantBId", tenantBId);
        seedCmd.Parameters.AddWithValue("branchBId", branchBId);
        seedCmd.Parameters.AddWithValue("sessionBId", sessionBId);
        seedCmd.Parameters.AddWithValue("tableBId", tableBId);
        seedCmd.Parameters.AddWithValue("keyB", $"key-B-{Guid.NewGuid():N}");

        await seedCmd.ExecuteNonQueryAsync();

        // 3. Connect as unprivileged runtime user with Tenant A context
        await using var runtimeConn = new NpgsqlConnection(runtimeConnStr);
        await runtimeConn.OpenAsync();

        await using var tx = await runtimeConn.BeginTransactionAsync();
        await using var setConfigCmd = runtimeConn.CreateCommand();
        setConfigCmd.Transaction = tx;
        setConfigCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";
        setConfigCmd.Parameters.AddWithValue("tenantId", tenantAId.ToString());
        await setConfigCmd.ExecuteNonQueryAsync();

        await using var queryCmd = runtimeConn.CreateCommand();
        queryCmd.Transaction = tx;
        queryCmd.CommandText = "SELECT id FROM tenancy.orders;";
        await using var reader = await queryCmd.ExecuteReaderAsync();

        var visibleOrderIds = new List<Guid>();
        while (await reader.ReadAsync())
        {
            visibleOrderIds.Add(reader.GetGuid(0));
        }

        Assert.Contains(orderAId, visibleOrderIds);
        Assert.DoesNotContain(orderBId, visibleOrderIds);
    }
}
