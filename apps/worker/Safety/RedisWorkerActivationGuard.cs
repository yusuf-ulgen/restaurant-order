using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Dynamic, Redis-backed worker activation guard.
/// Replaces static container startup environment variables with a centralized,
/// verifiable active slot state key in Redis (restaurant-order:active-slot).
/// In Production/Staging, fails closed if Redis is unreachable or the state key is missing.
/// </summary>
public class RedisWorkerActivationGuard : IWorkerActivationGuard, IDisposable
{
    public const string DefaultActiveSlotKey = "restaurant-order:active-slot";

    private readonly IConfiguration? _configuration;
    private readonly IHostEnvironment? _environment;
    private readonly ILogger<RedisWorkerActivationGuard> _logger;
    private IConnectionMultiplexer? _multiplexer;
    private readonly bool _ownsMultiplexer;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private string _cachedActiveSlot = "unconfigured";
    private WorkerActivationStatus _cachedStatus = WorkerActivationStatus.Standby;
    private string _lastLoggedState = "";

    public string SlotColor => ResolveSlotColor();

    public string ActiveSlot => _cachedActiveSlot;

    public WorkerActivationStatus Status => _cachedStatus;

    public RedisWorkerActivationGuard(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<RedisWorkerActivationGuard> logger,
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
    public RedisWorkerActivationGuard(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisWorkerActivationGuard> logger,
        string slotColor = "blue")
        : this(null!, null!, logger, multiplexer)
    {
        _cachedActiveSlot = slotColor;
    }

    public async Task<bool> IsActiveSlotAsync(CancellationToken cancellationToken = default)
    {
        var slotColor = SlotColor;
        var isStrict = IsStrictEnvironment();

        try
        {
            var db = await GetDatabaseAsync(cancellationToken);
            if (db == null)
            {
                if (isStrict)
                {
                    UpdateState("unconfigured", WorkerActivationStatus.Error,
                        $"[FAIL-CLOSED] Redis connection unavailable in strict environment. Slot '{slotColor}' cannot verify active slot.");
                    return false;
                }
                return FallbackToConfiguration(slotColor);
            }

            var redisValue = await db.StringGetAsync(DefaultActiveSlotKey);
            if (redisValue.IsNullOrEmpty)
            {
                if (isStrict)
                {
                    UpdateState("unconfigured", WorkerActivationStatus.Error,
                        $"[FAIL-CLOSED] Redis key '{DefaultActiveSlotKey}' is missing in strict environment. Slot '{slotColor}' paused.");
                    return false;
                }
                return FallbackToConfiguration(slotColor);
            }

            var rawSlot = redisValue.ToString().Trim().ToLowerInvariant();
            if (rawSlot != "blue" && rawSlot != "green")
            {
                UpdateState(rawSlot, WorkerActivationStatus.Error,
                    $"[FAIL-CLOSED] Redis active slot value '{rawSlot}' is invalid. Must be 'blue' or 'green'. Slot '{slotColor}' paused.");
                return false;
            }

            var isActive = string.Equals(slotColor, rawSlot, StringComparison.OrdinalIgnoreCase);
            var status = isActive ? WorkerActivationStatus.Active : WorkerActivationStatus.Standby;

            UpdateState(rawSlot, status,
                $"[STATE-TRANSITION] Slot '{slotColor}' status is now '{status}' (Central active slot: '{rawSlot}').");

            return isActive;
        }
        catch (Exception ex)
        {
            UpdateState("error", WorkerActivationStatus.Error,
                $"[FAIL-CLOSED] Exception querying active slot from Redis: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    private void UpdateState(string activeSlot, WorkerActivationStatus status, string stateMessage)
    {
        _cachedActiveSlot = activeSlot;
        _cachedStatus = status;

        var stateKey = $"{SlotColor}:{activeSlot}:{status}";
        if (_lastLoggedState != stateKey)
        {
            _lastLoggedState = stateKey;
            if (status == WorkerActivationStatus.Error)
            {
                _logger.LogError("{Message}", stateMessage);
            }
            else
            {
                _logger.LogInformation("{Message}", stateMessage);
            }
        }
    }

    private bool FallbackToConfiguration(string slotColor)
    {
        var configActive = _configuration?["ACTIVE_DEPLOYMENT_SLOT"]?.Trim().ToLowerInvariant() ?? slotColor;
        var isActive = string.Equals(slotColor, configActive, StringComparison.OrdinalIgnoreCase);
        var status = isActive ? WorkerActivationStatus.Active : WorkerActivationStatus.Standby;
        UpdateState(configActive, status,
            $"[DEV-FALLBACK] Active slot '{configActive}' resolved from configuration for slot '{slotColor}'.");
        return isActive;
    }

    private string ResolveSlotColor()
    {
        var color = _configuration?["DEPLOYMENT_COLOR"];
        if (string.IsNullOrWhiteSpace(color))
        {
            return _environment?.IsDevelopment() == true ? "blue" : "unconfigured";
        }
        return color.Trim().ToLowerInvariant();
    }

    private bool IsStrictEnvironment()
    {
        if (_environment == null) return false;
        return _environment.IsProduction() || _environment.IsEnvironment("Staging");
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
