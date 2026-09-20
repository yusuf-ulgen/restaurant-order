using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace RestaurantOrder.Api.Health;

/// <summary>
/// Verifies Redis cache accessibility by issuing a ping with fail-closed semantics.
/// Connection strings, credentials, and host details are strictly excluded from all log streams and outputs.
/// </summary>
public class StackExchangeRedisHealthCheck : IRedisHealthCheck
{
    private readonly IRedisConnectionProvider _connectionProvider;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StackExchangeRedisHealthCheck> _logger;

    public StackExchangeRedisHealthCheck(
        IRedisConnectionProvider connectionProvider,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<StackExchangeRedisHealthCheck> logger)
    {
        _connectionProvider = connectionProvider;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        var rawUrl = _configuration["REDIS_URL"] ?? _configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogWarning("REDIS_URL not configured. Development mode bypassing Redis health check.");
                return true;
            }

            _logger.LogError("REDIS_URL missing in non-development environment. Readiness check failed closed.");
            return false;
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var multiplexer = await _connectionProvider.GetConnectionAsync(linkedCts.Token);
            if (multiplexer == null || !multiplexer.IsConnected)
            {
                if (_environment.IsDevelopment())
                {
                    return true;
                }

                _logger.LogError("Redis connection unavailable. Health probe failed closed.");
                return false;
            }

            var db = multiplexer.GetDatabase();
            var pingResult = await db.PingAsync();

            return pingResult != TimeSpan.Zero;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Redis health check cancelled by caller.");
            return false;
        }
        catch (Exception ex)
        {
            // Sanitized logging: log exception type only, never ex.Message which may leak endpoints or auth info
            _logger.LogError("Redis health check failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }
}
