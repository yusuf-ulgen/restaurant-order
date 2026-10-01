using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Auth;
using StackExchange.Redis;

namespace RestaurantOrder.Infrastructure.Redis;

/// <summary>
/// Thread-safe singleton managing the StackExchange.Redis ConnectionMultiplexer and IDatabase.
/// Guarantees fail-closed behavior for security components when Redis is unavailable.
/// </summary>
public sealed class StackExchangeRedisDatabaseProvider : IRedisDatabaseProvider, IAsyncDisposable, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StackExchangeRedisDatabaseProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnectionMultiplexer? _multiplexer;
    private bool _disposed;

    public StackExchangeRedisDatabaseProvider(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<StackExchangeRedisDatabaseProvider> logger,
        IConnectionMultiplexer? multiplexer = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _multiplexer = multiplexer;
    }

    public async Task<IDatabase?> GetDatabaseAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_multiplexer != null && _multiplexer.IsConnected)
        {
            return _multiplexer.GetDatabase();
        }

        var rawUrl = _configuration["REDIS_URL"] ?? _configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            _logger.LogWarning("REDIS_URL missing. Distributed Redis operations unavailable.");
            return null;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_multiplexer != null && _multiplexer.IsConnected)
            {
                return _multiplexer.GetDatabase();
            }

            var options = ConfigurationOptions.Parse(rawUrl);
            options.ConnectTimeout = 3000;
            options.AsyncTimeout = 3000;
            options.AbortOnConnectFail = false;

            _multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
            return _multiplexer.GetDatabase();
        }
        catch (Exception ex)
        {
            _logger.LogError("Redis connection initialization failed: {ExceptionType}", ex.GetType().Name);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IDatabase> GetRequiredDatabaseAsync(CancellationToken ct = default)
    {
        var db = await GetDatabaseAsync(ct);
        if (db == null)
        {
            throw new DistributedSecurityStateUnavailableException(
                "Distributed Redis security infrastructure is unavailable. Request cannot proceed under fail-closed security policy.");
        }

        return db;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _multiplexer?.Dispose();
        _lock.Dispose();
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
    }
}
