using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public partial class CatalogAvailabilityOutboxIntegrationTests
{
    private async Task<string> GetRuntimeConnectionStringAsync()
    {
        await EnsureMigrationsAppliedAsync();
        return await _fixture.ProvisionTemporaryRuntimeRoleAsync();
    }

    private async Task<RestaurantOrderDbContext> CreateRuntimeDbContextAsync(Guid tenantId)
    {
        var connStr = await GetRuntimeConnectionStringAsync();
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new RestaurantOrderDbContext(options, new TenantContext(tenantId, isAuthenticated: true));
    }

    [Fact]
    public async Task RuntimeRoleRls_RolSuperAndRolBypassRls_AreFalse()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        var runtimeConnStr = await GetRuntimeConnectionStringAsync();

        await using var conn = new NpgsqlConnection(runtimeConnStr);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT r.rolname, r.rolsuper, r.rolbypassrls,
                   pg_has_role(current_user, 'restaurant_app_runtime', 'MEMBER') AS is_runtime_member
            FROM pg_roles r
            WHERE r.rolname = current_user;";

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Current runtime user must exist in pg_roles.");

        var rolName = reader.GetString(0);
        var rolSuper = reader.GetBoolean(1);
        var rolBypassRls = reader.GetBoolean(2);
        var isMember = reader.GetBoolean(3);

        Assert.False(rolSuper, $"Runtime user '{rolName}' must have rolsuper = false.");
        Assert.False(rolBypassRls, $"Runtime user '{rolName}' must have rolbypassrls = false.");
        Assert.True(isMember, $"Runtime user '{rolName}' must be a member of restaurant_app_runtime role.");
    }

    [Fact]
    public async Task RuntimeRoleRls_Outbox_TenantACanOnlySeeTenantA_AndCannotSeeTenantB_EvenWithIgnoreQueryFilters()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-rls-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-rls-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantA, branchA, "Branch A", $"ba-rls-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantB, branchB, "Branch B", $"bb-rls-{Guid.NewGuid():N}");

        var outboxA = CatalogAvailabilityOutboxMessage.Create(
            new TenantId(tenantA),
            new BranchId(branchA),
            "item-86-a",
            "CatalogItemQuick86",
            "{\"tenant\":\"A\"}",
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var outboxB = CatalogAvailabilityOutboxMessage.Create(
            new TenantId(tenantB),
            new BranchId(branchB),
            "item-86-b",
            "CatalogItemQuick86",
            "{\"tenant\":\"B\"}",
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        // Seed via owner connection
        await using (var ownerDb = CreateDbContext(tenantA))
        {
            ownerDb.CatalogAvailabilityOutbox.Add(outboxA);
            await ownerDb.SaveChangesAsync();
        }
        await using (var ownerDb = CreateDbContext(tenantB))
        {
            ownerDb.CatalogAvailabilityOutbox.Add(outboxB);
            await ownerDb.SaveChangesAsync();
        }

        // Query using runtime unprivileged role with Tenant A session context
        await using var dbA = await CreateRuntimeDbContextAsync(tenantA);
        await using var txA = await dbA.Database.BeginTransactionAsync();
        await dbA.SetTenantSessionAsync(tenantA);

        // IgnoreQueryFilters bypasses EF application filter, exposing only what PostgreSQL RLS allows
        var visibleMessages = await dbA.CatalogAvailabilityOutbox
            .IgnoreQueryFilters()
            .ToListAsync();

        Assert.Contains(visibleMessages, m => m.Id == outboxA.Id);
        Assert.DoesNotContain(visibleMessages, m => m.Id == outboxB.Id);

        // Explicit query for Tenant B ID via Tenant A context must return null
        var directB = await dbA.CatalogAvailabilityOutbox
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == outboxB.Id);
        Assert.Null(directB);
    }

    [Fact]
    public async Task RuntimeRoleRls_Outbox_RawSql_CannotRevealTenantBData()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-sql-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-sql-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantA, branchA, "Branch A", $"ba-sql-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantB, branchB, "Branch B", $"bb-sql-{Guid.NewGuid():N}");

        var outboxB = CatalogAvailabilityOutboxMessage.Create(
            new TenantId(tenantB),
            new BranchId(branchB),
            "item-86-b",
            "CatalogItemQuick86",
            "{\"secret\":\"tenant-b-data\"}",
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await using (var ownerDb = CreateDbContext(tenantB))
        {
            ownerDb.CatalogAvailabilityOutbox.Add(outboxB);
            await ownerDb.SaveChangesAsync();
        }

        // Open unprivileged runtime connection and set Tenant A context
        var runtimeConnStr = await GetRuntimeConnectionStringAsync();
        await using var conn = new NpgsqlConnection(runtimeConnStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.Transaction = tx;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tId, true);";
            setCmd.Parameters.AddWithValue("tId", tenantA.ToString());
            await setCmd.ExecuteNonQueryAsync();
        }

        // Direct raw SQL query on Tenant B's outbox row under Tenant A session must return 0
        await using (var queryCmd = conn.CreateCommand())
        {
            queryCmd.Transaction = tx;
            queryCmd.CommandText = "SELECT COUNT(*) FROM tenancy.catalog_availability_outbox WHERE id = @idB;";
            queryCmd.Parameters.AddWithValue("idB", outboxB.Id);
            var count = Convert.ToInt64(await queryCmd.ExecuteScalarAsync());
            Assert.Equal(0, count);
        }

        await using (var scanCmd = conn.CreateCommand())
        {
            scanCmd.Transaction = tx;
            scanCmd.CommandText = "SELECT COUNT(*) FROM tenancy.catalog_availability_outbox WHERE tenant_id = @tB;";
            scanCmd.Parameters.AddWithValue("tB", tenantB);
            var count = Convert.ToInt64(await scanCmd.ExecuteScalarAsync());
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task RuntimeRoleRls_Outbox_CannotInsertRecordForOtherTenant()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-ins-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-ins-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantB, branchB, "Branch B", $"bb-ins-{Guid.NewGuid():N}");

        var runtimeConnStr = await GetRuntimeConnectionStringAsync();
        await using var conn = new NpgsqlConnection(runtimeConnStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Bind transaction to Tenant A
        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.Transaction = tx;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tId, true);";
            setCmd.Parameters.AddWithValue("tId", tenantA.ToString());
            await setCmd.ExecuteNonQueryAsync();
        }

        // Attempting to insert an outbox row for Tenant B must be blocked by RLS WITH CHECK policy
        await using var insCmd = conn.CreateCommand();
        insCmd.Transaction = tx;
        insCmd.CommandText = @"
            INSERT INTO tenancy.catalog_availability_outbox
                (id, tenant_id, branch_id, aggregate_id, event_type, payload, idempotency_key, occurred_at, created_at, status, attempt_count, max_attempts, schema_version)
            VALUES
                (@id, @tenantB, @branchB, 'agg-cross', 'CatalogItemQuick86', '{}', @idemp, NOW(), NOW(), 0, 0, 5, 1);";
        insCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        insCmd.Parameters.AddWithValue("tenantB", tenantB);
        insCmd.Parameters.AddWithValue("branchB", branchB);
        insCmd.Parameters.AddWithValue("idemp", $"cross-tenant-{Guid.NewGuid():N}");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => insCmd.ExecuteNonQueryAsync());
        // PostgreSQL error 42501: insufficient_privilege (new row violates row-level security policy)
        Assert.Equal("42501", ex.SqlState);
        Assert.Contains("violates row-level security policy", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RuntimeRoleRls_Outbox_CannotUpdateOrDeleteRecordOfOtherTenant()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-upd-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-upd-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantB, branchB, "Branch B", $"bb-upd-{Guid.NewGuid():N}");

        var outboxB = CatalogAvailabilityOutboxMessage.Create(
            new TenantId(tenantB),
            new BranchId(branchB),
            "item-86-b",
            "CatalogItemQuick86",
            "{\"data\":\"b\"}",
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await using (var ownerDb = CreateDbContext(tenantB))
        {
            ownerDb.CatalogAvailabilityOutbox.Add(outboxB);
            await ownerDb.SaveChangesAsync();
        }

        var runtimeConnStr = await GetRuntimeConnectionStringAsync();
        await using var conn = new NpgsqlConnection(runtimeConnStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.Transaction = tx;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tId, true);";
            setCmd.Parameters.AddWithValue("tId", tenantA.ToString());
            await setCmd.ExecuteNonQueryAsync();
        }

        // UPDATE targeting Tenant B under Tenant A context affects 0 rows
        await using (var updCmd = conn.CreateCommand())
        {
            updCmd.Transaction = tx;
            updCmd.CommandText = "UPDATE tenancy.catalog_availability_outbox SET status = 1 WHERE id = @idB;";
            updCmd.Parameters.AddWithValue("idB", outboxB.Id);
            var affected = await updCmd.ExecuteNonQueryAsync();
            Assert.Equal(0, affected);
        }

        // DELETE targeting Tenant B under Tenant A context affects 0 rows
        await using (var delCmd = conn.CreateCommand())
        {
            delCmd.Transaction = tx;
            delCmd.CommandText = "DELETE FROM tenancy.catalog_availability_outbox WHERE id = @idB;";
            delCmd.Parameters.AddWithValue("idB", outboxB.Id);
            var affected = await delCmd.ExecuteNonQueryAsync();
            Assert.Equal(0, affected);
        }
    }

    [Fact]
    public async Task RuntimeRoleRls_Outbox_FailsClosed_WhenTenantContextUnsetOrEmpty()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var runtimeConnStr = await GetRuntimeConnectionStringAsync();
        await using var conn = new NpgsqlConnection(runtimeConnStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // 1. Completely unset tenant context
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM tenancy.catalog_availability_outbox;";
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(0, count);
        }

        // 2. Empty Guid context
        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.Transaction = tx;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', '00000000-0000-0000-0000-000000000000', true);";
            await setCmd.ExecuteNonQueryAsync();
        }
        await using (var emptyCmd = conn.CreateCommand())
        {
            emptyCmd.Transaction = tx;
            emptyCmd.CommandText = "SELECT COUNT(*) FROM tenancy.catalog_availability_outbox;";
            var count = Convert.ToInt64(await emptyCmd.ExecuteScalarAsync());
            Assert.Equal(0, count);
        }

        // 3. Invalid UUID string: fails safely without leaking data
        await using (var setInvalidCmd = conn.CreateCommand())
        {
            setInvalidCmd.Transaction = tx;
            setInvalidCmd.CommandText = "SELECT set_config('app.current_tenant_id', 'invalid-uuid-token', true);";
            await setInvalidCmd.ExecuteNonQueryAsync();
        }
        await using (var invalidCmd = conn.CreateCommand())
        {
            invalidCmd.Transaction = tx;
            invalidCmd.CommandText = "SELECT COUNT(*) FROM tenancy.catalog_availability_outbox;";
            // Postgres error 22P02: invalid input syntax for type uuid, or 0 rows returned
            try
            {
                var count = Convert.ToInt64(await invalidCmd.ExecuteScalarAsync());
                Assert.Equal(0, count);
            }
            catch (PostgresException pgEx)
            {
                Assert.Equal("22P02", pgEx.SqlState);
            }
        }
    }
}
