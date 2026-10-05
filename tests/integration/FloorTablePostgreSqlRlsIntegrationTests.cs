using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Verifies PostgreSQL Row-Level Security (RLS) data isolation, composite foreign keys,
/// and bypass immunity (raw SQL / IgnoreQueryFilters) for restaurant tables using the
/// unprivileged runtime application user ('restaurant_app_user').
/// </summary>
public class FloorTablePostgreSqlRlsIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public FloorTablePostgreSqlRlsIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<string> GetRuntimeConnectionStringAsync()
    {
        await EnsureMigrationsAppliedAsync();
        return await _fixture.ProvisionTemporaryRuntimeRoleAsync();
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

    private async Task<RestaurantOrderDbContext> CreateRuntimeDbContextAsync(ITenantContext? tenantContext = null)
    {
        var connStr = await GetRuntimeConnectionStringAsync();
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new RestaurantOrderDbContext(options, tenantContext);
    }

    [Fact]
    public async Task RLS_TenantA_CanOnlySee_TenantA_Tables_And_CannotSee_TenantB()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var branchAId = Guid.NewGuid();
        var branchBId = Guid.NewGuid();
        var diningAreaAId = Guid.NewGuid();
        var diningAreaBId = Guid.NewGuid();
        var tableAId = Guid.NewGuid();
        var tableBId = Guid.NewGuid();

        // 1. Seed data using owner connection
        await using (var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await ownerConn.OpenAsync();
            await using var cmd = ownerConn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
                VALUES (@tA, 'Tenant A', @slugA, 'Active', NOW(), gen_random_uuid()),
                       (@tB, 'Tenant B', @slugB, 'Active', NOW(), gen_random_uuid());

                INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
                VALUES (gen_random_uuid(), @tA, 'Brand A', @slugA, 'Active', NOW(), gen_random_uuid()),
                       (gen_random_uuid(), @tB, 'Brand B', @slugB, 'Active', NOW(), gen_random_uuid());

                INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
                VALUES (@bA, @tA, (SELECT id FROM tenancy.brands WHERE tenant_id = @tA LIMIT 1), 'Branch A', @slugA, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid()),
                       (@bB, @tB, (SELECT id FROM tenancy.brands WHERE tenant_id = @tB LIMIT 1), 'Branch B', @slugB, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid());

                INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
                VALUES (@daA, @tA, @bA, 'Area A', 'area-a', 1, 1, TRUE, NOW(), gen_random_uuid()),
                       (@daB, @tB, @bB, 'Area B', 'area-b', 1, 1, TRUE, NOW(), gen_random_uuid());

                INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
                VALUES (@tabA, @tA, @bA, @daA, 'T-01', 'Table 1', 4, 10, 10, 100, 100, 0, 1, TRUE, 1, gen_random_uuid(), NOW()),
                       (@tabB, @tB, @bB, @daB, 'T-02', 'Table 2', 4, 20, 20, 100, 100, 0, 1, TRUE, 1, gen_random_uuid(), NOW());";

            cmd.Parameters.AddWithValue("tA", tenantAId);
            cmd.Parameters.AddWithValue("tB", tenantBId);
            cmd.Parameters.AddWithValue("bA", branchAId);
            cmd.Parameters.AddWithValue("bB", branchBId);
            cmd.Parameters.AddWithValue("daA", diningAreaAId);
            cmd.Parameters.AddWithValue("daB", diningAreaBId);
            cmd.Parameters.AddWithValue("tabA", tableAId);
            cmd.Parameters.AddWithValue("tabB", tableBId);
            cmd.Parameters.AddWithValue("slugA", $"ta-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("slugB", $"tb-{Guid.NewGuid():N}");
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Query as runtime user with Tenant A context
        var contextA = new TenantContext(tenantAId, isAuthenticated: true);
        await using var dbContextA = await CreateRuntimeDbContextAsync(contextA);
        await using var txA = await dbContextA.Database.BeginTransactionAsync();
        await dbContextA.SetTenantSessionAsync(tenantAId);

        var tablesForA = await dbContextA.RestaurantTables.ToListAsync();
        Assert.Single(tablesForA);
        Assert.Equal(tableAId, tablesForA[0].Id.Value);

        // 3. IgnoreQueryFilters() attempt CANNOT expose Tenant B tables
        var bypassAttempt = await dbContextA.RestaurantTables
            .IgnoreQueryFilters()
            .ToListAsync();
        Assert.Single(bypassAttempt);
        Assert.Equal(tableAId, bypassAttempt[0].Id.Value);

        // 4. Raw SQL query directly querying tenancy.restaurant_tables CANNOT expose Tenant B tables
        var rawSqlResults = await dbContextA.RestaurantTables
            .FromSqlRaw("SELECT * FROM tenancy.restaurant_tables")
            .ToListAsync();
        Assert.Single(rawSqlResults);
        Assert.Equal(tableAId, rawSqlResults[0].Id.Value);

        // 5. Cross-tenant update blocked by RLS
        var rowsAffected = await dbContextA.Database.ExecuteSqlRawAsync(
            "UPDATE tenancy.restaurant_tables SET name = 'Hacked' WHERE id = {0}", tableBId);
        Assert.Equal(0, rowsAffected);

        // 6. Cross-tenant delete blocked by RLS
        var rowsDeleted = await dbContextA.Database.ExecuteSqlRawAsync(
            "DELETE FROM tenancy.restaurant_tables WHERE id = {0}", tableBId);
        Assert.Equal(0, rowsDeleted);
    }

    [Fact]
    public async Task CompositeForeignKey_CrossBranch_DiningArea_ViolatesConstraint()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchAId = Guid.NewGuid();
        var branchBId = Guid.NewGuid();
        var diningAreaBId = Guid.NewGuid();

        // Seed tenant, two branches, and dining area in branch B
        await using (var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await ownerConn.OpenAsync();
            await using var cmd = ownerConn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
                VALUES (@tId, 'Composite Test Tenant', @slug, 'Active', NOW(), gen_random_uuid());

                INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
                VALUES (gen_random_uuid(), @tId, 'Brand', @slug, 'Active', NOW(), gen_random_uuid());

                INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
                VALUES (@bA, @tId, (SELECT id FROM tenancy.brands WHERE tenant_id = @tId LIMIT 1), 'Branch A', @slugA, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid()),
                       (@bB, @tId, (SELECT id FROM tenancy.brands WHERE tenant_id = @tId LIMIT 1), 'Branch B', @slugB, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid());

                INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
                VALUES (@daB, @tId, @bB, 'Area in Branch B', 'area-b', 1, 1, TRUE, NOW(), gen_random_uuid());";

            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("bA", branchAId);
            cmd.Parameters.AddWithValue("bB", branchBId);
            cmd.Parameters.AddWithValue("daB", diningAreaBId);
            cmd.Parameters.AddWithValue("slug", $"comp-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("slugA", $"ba-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("slugB", $"bb-{Guid.NewGuid():N}");
            await cmd.ExecuteNonQueryAsync();

            // Attempt to insert table in Branch A pointing to DiningArea in Branch B
            await using var invalidInsertCmd = ownerConn.CreateCommand();
            invalidInsertCmd.CommandText = @"
                INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
                VALUES (gen_random_uuid(), @tId, @bA, @daB, 'T-CROSS-FK', 'Cross Table', 4, 0, 0, 100, 100, 0, 1, TRUE, 1, gen_random_uuid(), NOW());";
            invalidInsertCmd.Parameters.AddWithValue("tId", tenantId);
            invalidInsertCmd.Parameters.AddWithValue("bA", branchAId);
            invalidInsertCmd.Parameters.AddWithValue("daB", diningAreaBId);

            var ex = await Assert.ThrowsAsync<PostgresException>(() => invalidInsertCmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
        }
    }
}
