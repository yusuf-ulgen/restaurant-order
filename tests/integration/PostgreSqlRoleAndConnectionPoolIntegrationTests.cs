using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying connection pooling safety, concurrent session isolation,
/// and database role privilege boundaries for PostgreSQL Row-Level Security (RLS).
/// </summary>
public class PostgreSqlRoleAndConnectionPoolIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PostgreSqlRoleAndConnectionPoolIntegrationTests(TestcontainersFixture fixture)
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

    [Fact]
    public async Task RLS_PooledConnection_DoesNotLeakContext_AcrossSequentialOperations()
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

        // Shared connection representing a pooled connection
        await using var pooledConn = new NpgsqlConnection(await GetRuntimeConnectionStringAsync());
        await pooledConn.OpenAsync();

        // Operation 1: Tenant A executes transaction
        await using (var txA = await pooledConn.BeginTransactionAsync())
        {
            await using var setCmd = pooledConn.CreateCommand();
            setCmd.Transaction = txA;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";
            setCmd.Parameters.AddWithValue("tenantId", tenantAId.ToString());
            await setCmd.ExecuteNonQueryAsync();

            await using var queryCmd = pooledConn.CreateCommand();
            queryCmd.Transaction = txA;
            queryCmd.CommandText = "SELECT id FROM tenancy.tenants;";
            var id = (Guid)(await queryCmd.ExecuteScalarAsync())!;
            Assert.Equal(tenantAId, id);

            await txA.CommitAsync();
        }

        // Operation 2: Query without setting tenant context on same connection
        // Must fail-closed (0 rows) because transaction-local set_config expired
        await using (var checkCmd = pooledConn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM tenancy.tenants;";
            var count = (long)(await checkCmd.ExecuteScalarAsync())!;
            Assert.Equal(0L, count);
        }

        // Operation 3: Tenant B executes transaction on same connection
        await using (var txB = await pooledConn.BeginTransactionAsync())
        {
            await using var setCmd = pooledConn.CreateCommand();
            setCmd.Transaction = txB;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";
            setCmd.Parameters.AddWithValue("tenantId", tenantBId.ToString());
            await setCmd.ExecuteNonQueryAsync();

            await using var queryCmd = pooledConn.CreateCommand();
            queryCmd.Transaction = txB;
            queryCmd.CommandText = "SELECT id FROM tenancy.tenants;";
            var id = (Guid)(await queryCmd.ExecuteScalarAsync())!;
            Assert.Equal(tenantBId, id);

            await txB.CommitAsync();
        }
    }

    [Fact]
    public async Task RLS_TransactionRollback_DoesNotLeakContext_ToSubsequentOperationOnPooledConnection()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await using (var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await ownerConn.OpenAsync();
            await using var cmd = ownerConn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
                VALUES (@tId, 'Tenant Rollback Test', @slug, 'Active', NOW(), gen_random_uuid());";
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("slug", $"rb-{Guid.NewGuid():N}");
            await cmd.ExecuteNonQueryAsync();
        }

        await using var pooledConn = new NpgsqlConnection(await GetRuntimeConnectionStringAsync());
        await pooledConn.OpenAsync();

        // Transaction rolls back
        await using (var tx = await pooledConn.BeginTransactionAsync())
        {
            await using var setCmd = pooledConn.CreateCommand();
            setCmd.Transaction = tx;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";
            setCmd.Parameters.AddWithValue("tenantId", tenantId.ToString());
            await setCmd.ExecuteNonQueryAsync();

            await tx.RollbackAsync();
        }

        // Subsequent query on same connection must not have tenant context
        await using (var checkCmd = pooledConn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM tenancy.tenants;";
            var count = (long)(await checkCmd.ExecuteScalarAsync())!;
            Assert.Equal(0L, count);
        }
    }

    [Fact]
    public async Task RLS_ExceptionInRequest_DoesNotLeakContext_ToSubsequentOperationOnPooledConnection()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await using (var ownerConn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await ownerConn.OpenAsync();
            await using var cmd = ownerConn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
                VALUES (@tId, 'Tenant Exception Test', @slug, 'Active', NOW(), gen_random_uuid());";
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("slug", $"ex-{Guid.NewGuid():N}");
            await cmd.ExecuteNonQueryAsync();
        }

        await using var pooledConn = new NpgsqlConnection(await GetRuntimeConnectionStringAsync());
        await pooledConn.OpenAsync();

        // Simulate an exception occurring during request execution inside transaction
        try
        {
            await using var tx = await pooledConn.BeginTransactionAsync();
            await using var setCmd = pooledConn.CreateCommand();
            setCmd.Transaction = tx;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";
            setCmd.Parameters.AddWithValue("tenantId", tenantId.ToString());
            await setCmd.ExecuteNonQueryAsync();

            // Simulate unhandled exception before commit
            throw new InvalidOperationException("Simulated business exception");
        }
        catch (InvalidOperationException)
        {
            // Expected simulation
        }

        // Subsequent query on same connection must be clean (fail-closed, 0 rows)
        await using (var checkCmd = pooledConn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM tenancy.tenants;";
            var count = (long)(await checkCmd.ExecuteScalarAsync())!;
            Assert.Equal(0L, count);
        }
    }

    [Fact]
    public async Task RLS_ConcurrentOperations_DoNotLeakContext()
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

        var runtimeConnStr = await GetRuntimeConnectionStringAsync();

        // Run 10 parallel tasks alternating between Tenant A and Tenant B
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var targetTenant = i % 2 == 0 ? tenantAId : tenantBId;

            await using var conn = new NpgsqlConnection(runtimeConnStr);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await using var setCmd = conn.CreateCommand();
            setCmd.Transaction = tx;
            setCmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";
            setCmd.Parameters.AddWithValue("tenantId", targetTenant.ToString());
            await setCmd.ExecuteNonQueryAsync();

            await Task.Delay(10); // simulate concurrent work

            await using var queryCmd = conn.CreateCommand();
            queryCmd.Transaction = tx;
            queryCmd.CommandText = "SELECT id FROM tenancy.tenants;";
            var id = (Guid)(await queryCmd.ExecuteScalarAsync())!;
            Assert.Equal(targetTenant, id);

            await tx.CommitAsync();
        });

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task RLS_RuntimeRole_CannotAlterSchema_Or_DisableRowLevelSecurity()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        await using var runtimeConn = new NpgsqlConnection(await GetRuntimeConnectionStringAsync());
        await runtimeConn.OpenAsync();

        // 1. Runtime role cannot disable RLS
        await using var disableRlsCmd = runtimeConn.CreateCommand();
        disableRlsCmd.CommandText = "ALTER TABLE tenancy.tenants DISABLE ROW LEVEL SECURITY;";
        var ex1 = await Assert.ThrowsAsync<PostgresException>(() => disableRlsCmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex1.SqlState); // insufficient_privilege

        // 2. Runtime role cannot drop RLS policy
        await using var dropPolicyCmd = runtimeConn.CreateCommand();
        dropPolicyCmd.CommandText = "DROP POLICY tenant_isolation_policy ON tenancy.tenants;";
        var ex2 = await Assert.ThrowsAsync<PostgresException>(() => dropPolicyCmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex2.SqlState); // insufficient_privilege

        // 3. Runtime role cannot create tables in schema tenancy
        await using var createTableCmd = runtimeConn.CreateCommand();
        createTableCmd.CommandText = "CREATE TABLE tenancy.exploit (id int);";
        var ex3 = await Assert.ThrowsAsync<PostgresException>(() => createTableCmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex3.SqlState); // insufficient_privilege

        // 4. Verify runtime role strictly has NOBYPASSRLS and NOSUPERUSER
        await using var privCmd = runtimeConn.CreateCommand();
        privCmd.CommandText = "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = CURRENT_USER;";
        await using var reader = await privCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var isSuper = reader.GetBoolean(0);
        var isBypassRls = reader.GetBoolean(1);
        Assert.False(isSuper);
        Assert.False(isBypassRls);
    }
}
