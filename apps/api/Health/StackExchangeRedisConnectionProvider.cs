using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace RestaurantOrder.Api.Health;

/// <summary>
/// Thread-safe singleton provider managing the shared StackExchange.Redis ConnectionMultiplexer.
/// Eliminates per-probe connection churn and ensures sanitized logging without endpoint leaks.
/// </summary>
public class StackExchangeRedisConnectionProvider : IRedisConnectionProvider
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StackExchangeRedisConnectionProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnectionMultiplexer? _multiplexer;
    private bool _disposed;

    public StackExchangeRedisConnectionProvider(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<StackExchangeRedisConnectionProvider> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_multiplexer != null)
        {
            return _multiplexer;
        }

        var rawUrl = _configuration["REDIS_URL"] ?? _configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogWarning("REDIS_URL not configured. Development mode bypassing Redis connection establishment.");
                return null;
            }

            _logger.LogError("REDIS_URL missing in non-development environment. Redis connection provider failed closed.");
            return null;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_multiplexer != null)
            {
                return _multiplexer;
            }

            var options = ConfigurationOptions.Parse(rawUrl);
            options.ConnectTimeout = 3000;
            options.AsyncTimeout = 3000;
            options.AbortOnConnectFail = false;

            _multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
            return _multiplexer;
        }
        catch (Exception ex)
        {
            // Sanitized logging: log only the exception type, never connection string, endpoints, or host names
            _logger.LogError("Redis connection initialization failed: {ExceptionType}", ex.GetType().Name);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _multiplexer?.Dispose();
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_multiplexer != null)
        {
            await _multiplexer.DisposeAsync();
        }
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
