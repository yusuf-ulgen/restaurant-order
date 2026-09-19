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
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StackExchangeRedisHealthCheck> _logger;

    public StackExchangeRedisHealthCheck(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<StackExchangeRedisHealthCheck> logger)
    {
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
            var options = ConfigurationOptions.Parse(rawUrl);
            options.ConnectTimeout = 3000;
            options.AsyncTimeout = 3000;
            options.AbortOnConnectFail = false;

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
            if (!multiplexer.IsConnected)
            {
                _logger.LogError("Redis client failed to establish connection.");
                return false;
            }

            var db = multiplexer.GetDatabase();
            var pingResult = await db.PingAsync();

            return pingResult != TimeSpan.Zero;
        }
        catch (Exception ex)
        {
            // Sanitized logging: never include raw endpoint or passwords
            _logger.LogError("Redis health check failed: {ExceptionType} - {Message}",
                ex.GetType().Name,
                ex.Message);
            return false;
        }
    }
}
