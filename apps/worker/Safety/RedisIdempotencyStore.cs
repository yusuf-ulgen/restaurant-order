using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Production implementation of <see cref="IIdempotencyStore"/> backed by Redis.
/// Uses atomic SET NX + TTL to reserve idempotency keys and prevent duplicate processing
/// of queue events, printer spools, and payment webhooks across Blue-Green workers.
/// Fails closed if Redis is unreachable.
/// </summary>
public class RedisIdempotencyStore : IIdempotencyStore, IDisposable
{
    private const string KeyPrefix = "restaurant-order:idempotency:";
    private const string StatusInProgress = "IN_PROGRESS";
    private const string StatusCompleted = "COMPLETED";

    private readonly IConfiguration? _configuration;
    private readonly IHostEnvironment? _environment;
    private readonly ILogger<RedisIdempotencyStore> _logger;
    private IConnectionMultiplexer? _multiplexer;
    private readonly bool _ownsMultiplexer;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public RedisIdempotencyStore(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<RedisIdempotencyStore> logger,
        IConnectionMultiplexer? multiplexer = null)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
        _multiplexer = multiplexer;
        _ownsMultiplexer = multiplexer == null;
    }

    /// <summary>
    /// Test-friendly constructor accepting an explicit connection multiplexer.
    /// </summary>
    public RedisIdempotencyStore(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisIdempotencyStore> logger)
        : this(null!, null!, logger, multiplexer)
    {
    }

    public async Task<bool> TryReserveKeyAsync(
        string idempotencyKey,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key cannot be null or whitespace.", nameof(idempotencyKey));
        }

        try
        {
            var db = await GetDatabaseAsync(cancellationToken);
            if (db == null)
            {
                _logger.LogError("Redis unavailable. Rejecting idempotency reservation for '{Key}' (fail-closed).", idempotencyKey);
                return false;
            }

            var fullKey = $"{KeyPrefix}{idempotencyKey.Trim()}";
            var reserved = await db.StringSetAsync(fullKey, StatusInProgress, retention, When.NotExists);

            if (!reserved)
            {
                _logger.LogWarning("Duplicate job execution prevented by idempotency store for key '{Key}'.", idempotencyKey);
            }

            return reserved;
        }
        catch (Exception ex)
        {
            _logger.LogError("Idempotency reservation error for key '{Key}': {ExceptionType} - {Message}",
                idempotencyKey, ex.GetType().Name, ex.Message);
            return false;
        }
    }

    public async Task MarkCompletedAsync(
        string idempotencyKey,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return;

        try
        {
            var db = await GetDatabaseAsync(cancellationToken);
            if (db == null) return;

            var fullKey = $"{KeyPrefix}{idempotencyKey.Trim()}";
            await db.StringSetAsync(fullKey, StatusCompleted, retention);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error marking idempotency key '{Key}' completed: {ExceptionType} - {Message}",
                idempotencyKey, ex.GetType().Name, ex.Message);
        }
    }

    public async Task<bool> IsCompletedAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return false;

        try
        {
            var db = await GetDatabaseAsync(cancellationToken);
            if (db == null) return false;

            var fullKey = $"{KeyPrefix}{idempotencyKey.Trim()}";
            var value = await db.StringGetAsync(fullKey);

            return value == StatusCompleted;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error checking completion of idempotency key '{Key}': {ExceptionType} - {Message}",
                idempotencyKey, ex.GetType().Name, ex.Message);
            return false;
        }
    }

    private async Task<IDatabase?> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_multiplexer != null && _multiplexer.IsConnected)
        {
            return _multiplexer.GetDatabase();
        }

        if (_configuration == null)
        {
            return null;
        }

        var rawUrl = _configuration["REDIS_URL"] ?? _configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        await _connectionLock.WaitAsync(cancellationToken);
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
            return _multiplexer.IsConnected ? _multiplexer.GetDatabase() : null;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public void Dispose()
    {
        if (_ownsMultiplexer)
        {
            _multiplexer?.Dispose();
        }
        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
