using System.Diagnostics;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Manages lifecycle and disposable instances of PostgreSQL 16 and Redis 7 containers.
/// Provides real container connection strings, dynamic unprivileged runtime role provisioning,
/// and cleanup guarantees for integration tests.
/// </summary>
public class TestcontainersFixture : ITestDatabaseFixture, IAsyncLifetime
{
    private readonly PostgreSqlContainer? _postgresContainer;
    private readonly RedisContainer? _redisContainer;
    private readonly SemaphoreSlim _roleLock = new(1, 1);
    private bool _isStarted;
    private string? _runtimeUsername;
    private string? _runtimePassword;
    private string? _runtimeConnectionString;

    public bool IsDockerRunning { get; private set; }
    public Exception? InitializationException { get; private set; }
    public string DatabaseConnectionString => _isStarted && _postgresContainer != null ? _postgresContainer.GetConnectionString() : string.Empty;
    public string RedisEndpoint => _isStarted && _redisContainer != null ? _redisContainer.GetConnectionString() : string.Empty;

    /// <summary>
    /// Provisions a unique, temporary unprivileged application LOGIN role with a cryptographically random
    /// password for the duration of the test run. The role is a member of the NOLOGIN 'restaurant_app_runtime' group.
    /// Credentials are never logged, stored in files, or reused.
    /// </summary>
    public async Task<string> ProvisionTemporaryRuntimeRoleAsync()
    {
        if (_runtimeConnectionString != null)
        {
            return _runtimeConnectionString;
        }

        await _roleLock.WaitAsync();
        try
        {
            if (_runtimeConnectionString != null)
            {
                return _runtimeConnectionString;
            }

            var randomSuffix = Guid.NewGuid().ToString("N")[..12];
            _runtimeUsername = $"test_rt_{randomSuffix}";

            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            _runtimePassword = Convert.ToBase64String(bytes).Replace('+', '_').Replace('/', '-').TrimEnd('=');

            await using (var conn = new NpgsqlConnection(DatabaseConnectionString))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    DO $$
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{_runtimeUsername}') THEN
                            CREATE ROLE ""{_runtimeUsername}"" WITH LOGIN PASSWORD '{_runtimePassword}'
                                NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS IN ROLE restaurant_app_runtime;
                        END IF;
                    END $$;";
                await cmd.ExecuteNonQueryAsync();
            }

            var builder = new NpgsqlConnectionStringBuilder(DatabaseConnectionString)
            {
                Username = _runtimeUsername,
                Password = _runtimePassword
            };
            _runtimeConnectionString = builder.ConnectionString;
            return _runtimeConnectionString;
        }
        finally
        {
            _roleLock.Release();
        }
    }

    public TestcontainersFixture()
    {
        IsDockerRunning = CheckDockerAvailability();

        if (IsDockerRunning)
        {
            try
            {
                _postgresContainer = new PostgreSqlBuilder("postgres:16-alpine")
                    .WithDatabase("restaurant_order_test")
                    .WithUsername("test_user")
                    .WithPassword("test_pass_123!")
                    .WithCleanUp(true)
                    .Build();

                _redisContainer = new RedisBuilder("redis:7-alpine")
                    .WithCleanUp(true)
                    .Build();
            }
            catch (Exception ex)
            {
                IsDockerRunning = false;
                InitializationException = ex;
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning || _postgresContainer == null || _redisContainer == null)
        {
            return;
        }

        try
        {
            await Task.WhenAll(_postgresContainer.StartAsync(), _redisContainer.StartAsync());
            _isStarted = true;

            // Provision the unprivileged NOLOGIN group role 'restaurant_app_runtime'
            // required by the database schema migrations.
            await using (var conn = new NpgsqlConnection(_postgresContainer.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    DO $$
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_app_runtime') THEN
                            CREATE ROLE restaurant_app_runtime WITH NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                        END IF;
                    END $$;";
                await cmd.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            _isStarted = false;
            IsDockerRunning = false;
            InitializationException = ex;
        }
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_runtimeUsername) && !string.IsNullOrEmpty(DatabaseConnectionString))
        {
            try
            {
                await using var conn = new NpgsqlConnection(DatabaseConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    REASSIGN OWNED BY ""{_runtimeUsername}"" TO CURRENT_USER;
                    DROP OWNED BY ""{_runtimeUsername}"";
                    DROP ROLE IF EXISTS ""{_runtimeUsername}"";";
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best-effort cleanup of temporary role
            }
        }

        if (_isStarted)
        {
            var tasks = new List<Task>();
            if (_postgresContainer != null)
            {
                tasks.Add(_postgresContainer.DisposeAsync().AsTask());
            }
            if (_redisContainer != null)
            {
                tasks.Add(_redisContainer.DisposeAsync().AsTask());
            }

            await Task.WhenAll(tasks);
            _isStarted = false;
        }
    }

    public static bool CheckDockerAvailability()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return false;
            }

            var exited = process.WaitForExit(3000);
            return exited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
