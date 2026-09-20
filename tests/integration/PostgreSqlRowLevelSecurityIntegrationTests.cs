using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying PostgreSQL Row-Level Security (RLS) data isolation,
/// fail-closed behavior, and verification that neither IgnoreQueryFilters() nor raw SQL can bypass RLS.
/// Tests strictly execute with the non-owner runtime application role ('restaurant_app_user').
/// </summary>
public class PostgreSqlRowLevelSecurityIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PostgreSqlRowLevelSecurityIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private string GetRuntimeConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(_fixture.DatabaseConnectionString)
        {
            Username = "restaurant_app_user",
            Password = "app_secure_pass_123!"
        };
        return builder.ConnectionString;
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

    private RestaurantOrderDbContext CreateRuntimeDbContext(ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(GetRuntimeConnectionString())
            .Options;

        return new RestaurantOrderDbContext(options, tenantContext);
    }

    [Fact]
    public async Task RLS_TenantA_CanOnlySee_TenantA_Data_And_CannotSee_TenantB()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

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
                VALUES (gen_random_uuid(), @tA, 'Brand A', 'brand-a', 'Active', NOW(), gen_random_uuid()),
                       (gen_random_uuid(), @tB, 'Brand B', 'brand-b', 'Active', NOW(), gen_random_uuid());";
            cmd.Parameters.AddWithValue("tA", tenantAId);
            cmd.Parameters.AddWithValue("tB", tenantBId);
            cmd.Parameters.AddWithValue("slugA", $"ta-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("slugB", $"tb-{Guid.NewGuid():N}");
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Query as runtime user with Tenant A context
        var contextA = new TenantContext(tenantAId, isAuthenticated: true);
        await using var dbContextA = CreateRuntimeDbContext(contextA);
        await using var txA = await dbContextA.Database.BeginTransactionAsync();
        await dbContextA.SetTenantSessionAsync(tenantAId);

        var tenantsForA = await dbContextA.Tenants.ToListAsync();
        Assert.Single(tenantsForA);
        Assert.Equal(tenantAId, tenantsForA[0].Id.Value);

        var brandsForA = await dbContextA.Brands.ToListAsync();
        Assert.Single(brandsForA);
        Assert.Equal(tenantAId, brandsForA[0].TenantId.Value);

        // 3. Directly querying Tenant B's ID with Tenant A context returns 0 rows
        var tenantBQueriedByA = await dbContextA.Tenants.FirstOrDefaultAsync(t => t.Id == new TenantId(tenantBId));
        Assert.Null(tenantBQueriedByA);

        await txA.RollbackAsync();
    }

    [Fact]
    public async Task RLS_TenantA_CannotUpdateOrDelete_TenantB_Records()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var brandBId = Guid.NewGuid();

        // 1. Seed Tenant B data using owner connection
        await using (var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await ownerConn.OpenAsync();
            await using var cmd = ownerConn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
                VALUES (@tA, 'Tenant A', @slugA, 'Active', NOW(), gen_random_uuid()),
                       (@tB, 'Tenant B', @slugB, 'Active', NOW(), gen_random_uuid());
                INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
                VALUES (@bB, @tB, 'Brand B', 'brand-b-upd', 'Active', NOW(), gen_random_uuid());";
            cmd.Parameters.AddWithValue("tA", tenantAId);
            cmd.Parameters.AddWithValue("tB", tenantBId);
            cmd.Parameters.AddWithValue("bB", brandBId);
            cmd.Parameters.AddWithValue("slugA", $"ta-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("slugB", $"tb-{Guid.NewGuid():N}");
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Runtime user under Tenant A attempts to UPDATE Tenant B's brand
        await using var runtimeConn = new NpgsqlConnection(GetRuntimeConnectionString());
        await runtimeConn.OpenAsync();
        await using (var tx = await runtimeConn.BeginTransactionAsync())
        {
            await using var setCmd = runtimeConn.CreateCommand();
            setCmd.Transaction = tx;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";
            setCmd.Parameters.AddWithValue("tenantId", tenantAId.ToString());
            await setCmd.ExecuteNonQueryAsync();

            await using var updateCmd = runtimeConn.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = "UPDATE tenancy.brands SET name = 'Hacked' WHERE id = @bId;";
            updateCmd.Parameters.AddWithValue("bId", brandBId);
            var rowsAffected = await updateCmd.ExecuteNonQueryAsync();

            // Zero rows affected because RLS hides Brand B from Tenant A
            Assert.Equal(0, rowsAffected);

            // Attempt DELETE on Tenant B's brand
            await using var deleteCmd = runtimeConn.CreateCommand();
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM tenancy.brands WHERE id = @bId;";
            deleteCmd.Parameters.AddWithValue("bId", brandBId);
            var deleteRows = await deleteCmd.ExecuteNonQueryAsync();
            Assert.Equal(0, deleteRows);

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task RLS_MissingOrInvalid_TenantContext_FailsClosed()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        // Query using runtime user without setting app.current_tenant_id
        await using var runtimeConn = new NpgsqlConnection(GetRuntimeConnectionString());
        await runtimeConn.OpenAsync();

        // 1. Without tenant context: SELECT returns 0 rows
        await using (var cmd = runtimeConn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM tenancy.tenants;";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(0L, count);
        }

        // 2. Without tenant context: INSERT fails RLS WITH CHECK policy
        await using (var insertCmd = runtimeConn.CreateCommand())
        {
            insertCmd.CommandText = @"
                INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
                VALUES (gen_random_uuid(), 'Anonymous', 'anon-slug', 'Active', NOW(), gen_random_uuid());";
            var ex = await Assert.ThrowsAsync<PostgresException>(() => insertCmd.ExecuteNonQueryAsync());
            Assert.Equal("44000", ex.SqlState); // check_violation (RLS WITH CHECK violation)
        }

        // 3. With invalid string as tenant context: SELECT returns 0 rows (fail-closed)
        await using (var tx = await runtimeConn.BeginTransactionAsync())
        {
            await using var setCmd = runtimeConn.CreateCommand();
            setCmd.Transaction = tx;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', 'not-a-uuid-string', true);";
            await setCmd.ExecuteNonQueryAsync();

            await using var selectCmd = runtimeConn.CreateCommand();
            selectCmd.Transaction = tx;
            selectCmd.CommandText = "SELECT COUNT(*) FROM tenancy.tenants;";
            var count = (long)(await selectCmd.ExecuteScalarAsync())!;
            Assert.Equal(0L, count);

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task RLS_IgnoreQueryFilters_CannotBypass_RowLevelSecurity()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using (var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await ownerConn.OpenAsync();
            await using var cmd = ownerConn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
                VALUES (@tA, 'Tenant A', @slugA, 'Active', NOW(), gen_random_uuid()),
                       (@tB, 'Tenant B', @slugB, 'Active', NOW(), gen_random_uuid());";
            cmd.Parameters.AddWithValue("tA", tenantAId);
            cmd.Parameters.AddWithValue("tB", tenantBId);
            cmd.Parameters.AddWithValue("slugA", $"ta-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("slugB", $"tb-{Guid.NewGuid():N}");
            await cmd.ExecuteNonQueryAsync();
        }

        var contextA = new TenantContext(tenantAId, isAuthenticated: true);
        await using var dbContextA = CreateRuntimeDbContext(contextA);
        await using var tx = await dbContextA.Database.BeginTransactionAsync();
        await dbContextA.SetTenantSessionAsync(tenantAId);

        // Even though EF Core query filter is bypassed, PostgreSQL RLS restricts results to Tenant A
        var allTenants = await dbContextA.Tenants.IgnoreQueryFilters().ToListAsync();
        Assert.Single(allTenants);
        Assert.Equal(tenantAId, allTenants[0].Id.Value);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task RLS_RawSql_CannotBypass_RowLevelSecurity()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using (var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await ownerConn.OpenAsync();
            await using var cmd = ownerConn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
                VALUES (@tA, 'Tenant A', @slugA, 'Active', NOW(), gen_random_uuid()),
                       (@tB, 'Tenant B', @slugB, 'Active', NOW(), gen_random_uuid());";
            cmd.Parameters.AddWithValue("tA", tenantAId);
            cmd.Parameters.AddWithValue("tB", tenantBId);
            cmd.Parameters.AddWithValue("slugA", $"ta-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("slugB", $"tb-{Guid.NewGuid():N}");
            await cmd.ExecuteNonQueryAsync();
        }

        await using var runtimeConn = new NpgsqlConnection(GetRuntimeConnectionString());
        await runtimeConn.OpenAsync();
        await using var tx = await runtimeConn.BeginTransactionAsync();

        await using var setCmd = runtimeConn.CreateCommand();
        setCmd.Transaction = tx;
        setCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";
        setCmd.Parameters.AddWithValue("tenantId", tenantAId.ToString());
        await setCmd.ExecuteNonQueryAsync();

        // Raw unqualified SELECT * FROM tenancy.tenants
        await using var rawCmd = runtimeConn.CreateCommand();
        rawCmd.Transaction = tx;
        rawCmd.CommandText = "SELECT id, name FROM tenancy.tenants;";
        await using var reader = await rawCmd.ExecuteReaderAsync();

        var foundIds = new List<Guid>();
        while (await reader.ReadAsync())
        {
            foundIds.Add(reader.GetGuid(0));
        }

        Assert.Single(foundIds);
        Assert.Equal(tenantAId, foundIds[0]);

        await tx.RollbackAsync();
    }
}
