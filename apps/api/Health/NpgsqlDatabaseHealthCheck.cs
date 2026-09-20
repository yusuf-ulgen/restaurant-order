using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace RestaurantOrder.Api.Health;

/// <summary>
/// Verifies PostgreSQL database accessibility by executing a ping query with fail-closed semantics.
/// Connection strings, credentials, and host details are strictly excluded from all log streams and outputs.
/// </summary>
public class NpgsqlDatabaseHealthCheck : IDatabaseHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<NpgsqlDatabaseHealthCheck> _logger;

    public NpgsqlDatabaseHealthCheck(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<NpgsqlDatabaseHealthCheck> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        var rawConn = _configuration["DATABASE_URL"] ?? _configuration.GetConnectionString("Database");

        if (string.IsNullOrWhiteSpace(rawConn))
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogWarning("DATABASE_URL not configured. Development mode bypassing database health check.");
                return true;
            }

            _logger.LogError("DATABASE_URL missing in non-development environment. Readiness check failed closed.");
            return false;
        }

        try
        {
            var connectionString = NormalizeConnectionString(rawConn);
            await using var connection = new NpgsqlConnection(connectionString);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await connection.OpenAsync(linkedCts.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            var result = await command.ExecuteScalarAsync(linkedCts.Token);

            return result != null;
        }
        catch (Exception ex)
        {
            // Sanitized logging: never include raw connection string or passwords
            _logger.LogError("Database health check failed: {ExceptionType} - {Message}",
                ex.GetType().Name,
                ex.Message);
            return false;
        }
    }

    private static string NormalizeConnectionString(string raw)
    {
        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(raw);
            var userInfo = uri.UserInfo.Split(':');
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = uri.AbsolutePath.TrimStart('/'),
                Username = userInfo.Length > 0 ? userInfo[0] : "postgres",
                Password = userInfo.Length > 1 ? userInfo[1] : string.Empty,
                Timeout = 3,
                CommandTimeout = 3
            };
            return builder.ConnectionString;
        }

        var csBuilder = new NpgsqlConnectionStringBuilder(raw)
        {
            Timeout = 3,
            CommandTimeout = 3
        };
        return csBuilder.ConnectionString;
    }
}
