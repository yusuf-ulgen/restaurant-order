using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Production implementation of <see cref="IWorkerLeaseManager"/> backed by Redis.
/// Enforces distributed mutually-exclusive execution between Blue and Green worker instances.
/// Uses atomic SET NX + TTL for acquisition and Lua scripts for atomic compare-and-expire / compare-and-delete.
/// Never logs connection strings, passwords, or secrets. Fails closed on any Redis disconnection.
/// </summary>
public class RedisWorkerLeaseManager : IWorkerLeaseManager, IDisposable
{
    private const string RenewScript = @"
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('pexpire', KEYS[1], ARGV[2])
        else
            return 0
        end";

    private const string ReleaseScript = @"
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end";

    private readonly IConfiguration? _configuration;
    private readonly IHostEnvironment? _environment;
    private readonly ILogger<RedisWorkerLeaseManager> _logger;
    private IConnectionMultiplexer? _multiplexer;
    private readonly bool _ownsMultiplexer;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public string OwnerToken { get; }

    public RedisWorkerLeaseManager(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<RedisWorkerLeaseManager> logger,
        IConnectionMultiplexer? multiplexer = null,
        string? ownerToken = null)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
        _multiplexer = multiplexer;
        _ownsMultiplexer = multiplexer == null;
        OwnerToken = string.IsNullOrWhiteSpace(ownerToken) ? GenerateSecureToken() : ownerToken.Trim();
    }

    /// <summary>
    /// Test-friendly constructor accepting an explicit connection multiplexer.
    /// </summary>
    public RedisWorkerLeaseManager(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisWorkerLeaseManager> logger,
        string? ownerToken = null)
        : this(null!, null!, logger, multiplexer, ownerToken)
    {
    }

    public async Task<bool> TryAcquireLeaseAsync(
        string leaseKey,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = await GetDatabaseAsync(cancellationToken);
            if (db == null) return false;

            // SET key ownerToken PX <milliseconds> NX
            var acquired = await db.StringSetAsync(
                leaseKey,
                OwnerToken,
                duration,
                When.NotExists);

            if (acquired)
            {
                _logger.LogDebug("Acquired lease '{LeaseKey}' for duration {Duration}.", leaseKey, duration);
            }

            return acquired;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to acquire lease '{LeaseKey}': {ExceptionType} - {Message}",
                leaseKey, ex.GetType().Name, ex.Message);
            return false;
        }
    }

    public async Task<bool> RenewLeaseAsync(
        string leaseKey,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = await GetDatabaseAsync(cancellationToken);
            if (db == null) return false;

            var milliseconds = (long)duration.TotalMilliseconds;
            var result = (int)await db.ScriptEvaluateAsync(
                RenewScript,
                new RedisKey[] { leaseKey },
                new RedisValue[] { OwnerToken, milliseconds });

            var renewed = result == 1;
            if (!renewed)
            {
                _logger.LogWarning("Lease renewal rejected for '{LeaseKey}'. Token mismatch or lease expired.", leaseKey);
            }

            return renewed;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to renew lease '{LeaseKey}': {ExceptionType} - {Message}",
                leaseKey, ex.GetType().Name, ex.Message);
            return false;
        }
    }

    public async Task ReleaseLeaseAsync(
        string leaseKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = await GetDatabaseAsync(cancellationToken);
            if (db == null) return;

            var result = (int)await db.ScriptEvaluateAsync(
                ReleaseScript,
                new RedisKey[] { leaseKey },
                new RedisValue[] { OwnerToken });

            if (result == 1)
            {
                _logger.LogDebug("Released lease '{LeaseKey}'.", leaseKey);
            }
            else
            {
                _logger.LogWarning("Could not release lease '{LeaseKey}'. Token mismatch or lease already expired.", leaseKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to release lease '{LeaseKey}': {ExceptionType} - {Message}",
                leaseKey, ex.GetType().Name, ex.Message);
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
            _logger.LogCritical("Redis configuration is not available. Fail-closed.");
            return null;
        }

        var rawUrl = _configuration["REDIS_URL"] ?? _configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            var isStrict = _environment == null || _environment.IsProduction() || _environment.IsEnvironment("Staging");
            if (isStrict)
            {
                _logger.LogCritical("REDIS_URL missing in production/staging environment. Worker lease manager failed closed.");
            }
            else
            {
                _logger.LogWarning("REDIS_URL missing in development. Worker lease manager unavailable.");
            }
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

    private static string GenerateSecureToken()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
