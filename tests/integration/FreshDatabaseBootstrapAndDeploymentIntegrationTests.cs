using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Verifies the end-to-end production deployment sequence on a pristine, fresh PostgreSQL 16 instance:
/// 1. DBA / Operator executes deploy/bootstrap/001_create_runtime_login_role.sql via psql with secret injection.
/// 2. Schema migrations are applied with non-destructive DDL and role permission enforcement.
/// 3. Application connects via unprivileged LOGIN role and validates fail-closed Row-Level Security.
/// 4. Credential rotation and bootstrap idempotency are verified.
/// </summary>
public class FreshDatabaseBootstrapAndDeploymentIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _freshContainer;
    private readonly bool _dockerAvailable;
    private string _dbConnectionString = string.Empty;

    public FreshDatabaseBootstrapAndDeploymentIntegrationTests()
    {
        _dockerAvailable = TestcontainersFixture.CheckDockerAvailability();

        if (_dockerAvailable)
        {
            try
            {
                _freshContainer = new PostgreSqlBuilder("postgres:16-alpine")
                    .WithDatabase("fresh_bootstrap_test")
                    .WithUsername("postgres")
                    .WithPassword("dba_super_pass_123")
                    .WithCleanUp(true)
                    .Build();
            }
            catch
            {
                _dockerAvailable = false;
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (_dockerAvailable && _freshContainer != null)
        {
            await _freshContainer.StartAsync();
            _dbConnectionString = _freshContainer.GetConnectionString();

            // Copy the repository's bootstrap SQL file into the container
            var scriptPath = Path.Combine(FindRepositoryRoot(), "deploy", "bootstrap", "001_create_runtime_login_role.sql");
            var scriptBytes = await File.ReadAllBytesAsync(scriptPath);
            await _freshContainer.CopyAsync(scriptBytes, "/tmp/001_create_runtime_login_role.sql");
        }
    }

    public async Task DisposeAsync()
    {
        if (_freshContainer != null)
        {
            await _freshContainer.DisposeAsync();
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RestaurantOrder.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private RestaurantOrderDbContext CreateDbContext(string connectionString, ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(RestaurantOrderDbContext).Assembly.FullName);
            })
            .Options;

        return new RestaurantOrderDbContext(options, tenantContext);
    }

    [Fact]
    public async Task Migration_OnFreshDatabase_WithoutBootstrap_FailsFast_WithClearError()
    {
        if (!_dockerAvailable || _freshContainer == null) return;

        // Create a separate blank database inside the clean cluster where bootstrap has NOT run
        await using (var adminConn = new NpgsqlConnection(_dbConnectionString))
        {
            await adminConn.OpenAsync();
            await using var cmd = adminConn.CreateCommand();
            cmd.CommandText = "CREATE DATABASE unbootstrapped_db;";
            await cmd.ExecuteNonQueryAsync();
        }

        var unbootstrappedBuilder = new NpgsqlConnectionStringBuilder(_dbConnectionString)
        {
            Database = "unbootstrapped_db"
        };

        await using var context = CreateDbContext(unbootstrappedBuilder.ConnectionString);

        // Migration MUST fail fast because required cluster group role 'restaurant_app_runtime' does not exist
        var ex = await Assert.ThrowsAsync<PostgresException>(() => context.Database.MigrateAsync());
        Assert.Contains("restaurant_app_runtime", ex.MessageText);
        Assert.Contains("001_create_runtime_login_role.sql", ex.MessageText);
    }

    [Fact]
    public async Task Bootstrap_MissingOrEmptySecret_FailsFast()
    {
        if (!_dockerAvailable || _freshContainer == null) return;

        // 1. Missing / Unset APP_RUNTIME_PASSWORD -> must fail fast with exit code != 0
        var unsetResult = await _freshContainer.ExecAsync(new[]
        {
            "sh", "-c", "unset APP_RUNTIME_PASSWORD && psql -U postgres -d fresh_bootstrap_test -v ON_ERROR_STOP=1 -f /tmp/001_create_runtime_login_role.sql"
        });

        Assert.NotEqual(0, unsetResult.ExitCode);
        Assert.Contains("Environment variable APP_RUNTIME_PASSWORD is not set", unsetResult.Stderr);

        // 2. Empty APP_RUNTIME_PASSWORD -> must fail fast with exit code != 0
        var emptyResult = await _freshContainer.ExecAsync(new[]
        {
            "sh", "-c", "export APP_RUNTIME_PASSWORD='' && psql -U postgres -d fresh_bootstrap_test -v ON_ERROR_STOP=1 -f /tmp/001_create_runtime_login_role.sql"
        });

        Assert.NotEqual(0, emptyResult.ExitCode);
        Assert.Contains("Environment variable APP_RUNTIME_PASSWORD must be non-empty", emptyResult.Stderr);
    }

    [Fact]
    public async Task Bootstrap_And_Migration_EndToEnd_ProvisionsRoles_AndEnforcesRLS()
    {
        if (!_dockerAvailable || _freshContainer == null) return;

        // 1. Generate cryptographically secure random password
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var initialPassword = Convert.ToBase64String(bytes).Replace('+', '_').Replace('/', '-').TrimEnd('=') + "!9aZ";

        // 2. Execute bootstrap script with injected password via environment variable
        var bootstrapResult = await _freshContainer.ExecAsync(new[]
        {
            "sh", "-c", $"export APP_RUNTIME_PASSWORD='{initialPassword}' && psql -U postgres -d fresh_bootstrap_test -v ON_ERROR_STOP=1 -f /tmp/001_create_runtime_login_role.sql"
        });

        Assert.Equal(0, bootstrapResult.ExitCode);
        // Security requirement: Ensure the raw password is not reflected in outputs or logs
        Assert.DoesNotContain(initialPassword, bootstrapResult.Stdout);
        Assert.DoesNotContain(initialPassword, bootstrapResult.Stderr);

        // 3. Verify PostgreSQL security attributes of provisioned roles
        await using (var adminConn = new NpgsqlConnection(_dbConnectionString))
        {
            await adminConn.OpenAsync();

            // Verify 'restaurant_app_runtime' group role
            await using (var cmd = adminConn.CreateCommand())
            {
                cmd.CommandText = "SELECT rolcanlogin, rolsuper, rolcreaterole, rolcreatedb, rolbypassrls FROM pg_roles WHERE rolname = 'restaurant_app_runtime';";
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.False(reader.GetBoolean(0), "restaurant_app_runtime must be NOLOGIN");
                Assert.False(reader.GetBoolean(1), "restaurant_app_runtime must be NOSUPERUSER");
                Assert.False(reader.GetBoolean(2), "restaurant_app_runtime must be NOCREATEROLE");
                Assert.False(reader.GetBoolean(3), "restaurant_app_runtime must be NOCREATEDB");
                Assert.False(reader.GetBoolean(4), "restaurant_app_runtime must be NOBYPASSRLS");
            }

            // Verify 'restaurant_app_user' login role
            await using (var cmd = adminConn.CreateCommand())
            {
                cmd.CommandText = "SELECT rolcanlogin, rolsuper, rolcreaterole, rolcreatedb, rolbypassrls FROM pg_roles WHERE rolname = 'restaurant_app_user';";
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.True(reader.GetBoolean(0), "restaurant_app_user must be LOGIN");
                Assert.False(reader.GetBoolean(1), "restaurant_app_user must be NOSUPERUSER");
                Assert.False(reader.GetBoolean(2), "restaurant_app_user must be NOCREATEROLE");
                Assert.False(reader.GetBoolean(3), "restaurant_app_user must be NOCREATEDB");
                Assert.False(reader.GetBoolean(4), "restaurant_app_user must be NOBYPASSRLS");
            }

            // Verify role membership: restaurant_app_user is a member of restaurant_app_runtime
            await using (var cmd = adminConn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT 1 FROM pg_auth_members am
                    JOIN pg_roles m ON am.roleid = m.oid
                    JOIN pg_roles r ON am.member = r.oid
                    WHERE r.rolname = 'restaurant_app_user' AND m.rolname = 'restaurant_app_runtime';";
                var isMember = await cmd.ExecuteScalarAsync();
                Assert.NotNull(isMember);
            }
        }

        // 4. Verify literal '${APP_RUNTIME_PASSWORD}' was NOT used as password
        var literalPlaceholderBuilder = new NpgsqlConnectionStringBuilder(_dbConnectionString)
        {
            Username = "restaurant_app_user",
            Password = "${APP_RUNTIME_PASSWORD}"
        };
        await using (var literalConn = new NpgsqlConnection(literalPlaceholderBuilder.ConnectionString))
        {
            await Assert.ThrowsAsync<PostgresException>(() => literalConn.OpenAsync());
        }

        Guid tenantAId;
        Guid tenantBId;

        // 5. Apply EF Core schema migrations using DBA connection (Step 2 of deployment sequence)
        await using (var dbaContext = CreateDbContext(_dbConnectionString))
        {
            await dbaContext.Database.MigrateAsync();

            // Seed two separate tenants
            var tenantA = Tenant.Create("Tenant Alpha", $"tenant-a-{Guid.NewGuid():N}");
            var tenantB = Tenant.Create("Tenant Beta", $"tenant-b-{Guid.NewGuid():N}");
            tenantAId = tenantA.Id.Value;
            tenantBId = tenantB.Id.Value;
            dbaContext.Tenants.AddRange(tenantA, tenantB);
            await dbaContext.SaveChangesAsync();
        }

        // 6. Connect with provisioned runtime LOGIN role and verify fail-closed RLS (Step 4 of deployment sequence)
        var runtimeBuilder = new NpgsqlConnectionStringBuilder(_dbConnectionString)
        {
            Username = "restaurant_app_user",
            Password = initialPassword,
            Pooling = false
        };

        await using (var runtimeConn = new NpgsqlConnection(runtimeBuilder.ConnectionString))
        {
            await runtimeConn.OpenAsync();

            // 6.1. Without tenant context: SELECT returns 0 rows (fail-closed)
            await using (var cmd = runtimeConn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM tenancy.tenants;";
                var count = (long)(await cmd.ExecuteScalarAsync())!;
                Assert.Equal(0L, count);
            }

            // 6.2. With Tenant Alpha context: SELECT returns only Tenant Alpha
            await using (var tx = await runtimeConn.BeginTransactionAsync())
            {
                await using var setCmd = runtimeConn.CreateCommand();
                setCmd.Transaction = tx;
                setCmd.CommandText = $"SELECT set_config('app.current_tenant_id', '{tenantAId}', true);";
                await setCmd.ExecuteNonQueryAsync();

                await using var selectCmd = runtimeConn.CreateCommand();
                selectCmd.Transaction = tx;
                selectCmd.CommandText = "SELECT COUNT(*) FROM tenancy.tenants;";
                var count = (long)(await selectCmd.ExecuteScalarAsync())!;
                Assert.Equal(1L, count);

                // Cannot see Tenant Beta
                await using var betaCmd = runtimeConn.CreateCommand();
                betaCmd.Transaction = tx;
                betaCmd.CommandText = $"SELECT COUNT(*) FROM tenancy.tenants WHERE id = '{tenantBId}';";
                var betaCount = (long)(await betaCmd.ExecuteScalarAsync())!;
                Assert.Equal(0L, betaCount);

                await tx.RollbackAsync();
            }
        }

        // 7. Test Idempotency and Credential Rotation:
        // Re-running bootstrap with a new rotated password must succeed and rotate credentials without dropping privileges.
        var rotatedBytes = new byte[32];
        RandomNumberGenerator.Fill(rotatedBytes);
        var rotatedPassword = Convert.ToBase64String(rotatedBytes).Replace('+', '_').Replace('/', '-').TrimEnd('=') + "!Rot9";

        var rotationResult = await _freshContainer.ExecAsync(new[]
        {
            "sh", "-c", $"export APP_RUNTIME_PASSWORD='{rotatedPassword}' && psql -U postgres -d fresh_bootstrap_test -v ON_ERROR_STOP=1 -f /tmp/001_create_runtime_login_role.sql"
        });
        Assert.Equal(0, rotationResult.ExitCode);

        // Clear pools to ensure we test authentication with PostgreSQL directly
        NpgsqlConnection.ClearAllPools();

        // Old password must now fail
        await using (var oldConn = new NpgsqlConnection(runtimeBuilder.ConnectionString))
        {
            await Assert.ThrowsAsync<PostgresException>(() => oldConn.OpenAsync());
        }

        // Rotated password must connect and maintain access under RLS
        var rotatedBuilder = new NpgsqlConnectionStringBuilder(_dbConnectionString)
        {
            Username = "restaurant_app_user",
            Password = rotatedPassword,
            Pooling = false
        };
        await using (var rotatedConn = new NpgsqlConnection(rotatedBuilder.ConnectionString))
        {
            await rotatedConn.OpenAsync();
            await using var cmd = rotatedConn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM tenancy.tenants;";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(0L, count); // Still fail-closed without tenant context
        }
    }
}
