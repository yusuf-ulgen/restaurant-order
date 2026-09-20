using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Manages lifecycle and disposable instances of PostgreSQL 16 and Redis 7 containers.
/// Provides real container connection strings and cleanup guarantees for integration tests.
/// </summary>
public class TestcontainersFixture : ITestDatabaseFixture, IAsyncLifetime
{
    private readonly PostgreSqlContainer? _postgresContainer;
    private readonly RedisContainer? _redisContainer;
    private bool _isStarted;

    public bool IsDockerRunning { get; private set; }
    public Exception? InitializationException { get; private set; }
    public string DatabaseConnectionString => _isStarted && _postgresContainer != null ? _postgresContainer.GetConnectionString() : string.Empty;
    public string RedisEndpoint => _isStarted && _redisContainer != null ? _redisContainer.GetConnectionString() : string.Empty;

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

    private static bool CheckDockerAvailability()
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
