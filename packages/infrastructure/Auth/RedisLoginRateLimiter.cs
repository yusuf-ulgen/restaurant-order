using Microsoft.Extensions.Hosting;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Distributed Redis-backed rate limiter for authentication attempts.
/// Uses atomic Lua scripts and key namespacing with strict fail-closed behavior on Redis failure.
/// </summary>
public sealed class RedisLoginRateLimiter : ILoginRateLimiter
{
    private const string RecordAttemptLuaScript = @"
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return current";

    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly IHostEnvironment _environment;
    private readonly int _maxFailedAttempts;
    private readonly TimeSpan _window;

    public RedisLoginRateLimiter(
        IRedisDatabaseProvider redisProvider,
        IHostEnvironment environment,
        int maxFailedAttempts = 5,
        TimeSpan? window = null)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _maxFailedAttempts = maxFailedAttempts;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    public async Task<bool> IsRateLimitedAsync(
        string ipAddress,
        string normalizedEmail,
        string? tenantSlug,
        CancellationToken ct = default)
    {
        var db = await _redisProvider.GetRequiredDatabaseAsync(ct);
        var key = AuthRedisKeyBuilder.LoginRateLimit(_environment.EnvironmentName, ipAddress, normalizedEmail, tenantSlug);

        var val = await db.StringGetAsync(key);
        if (val.IsNullOrEmpty)
        {
            return false;
        }

        if (int.TryParse((string?)val, out var count))
        {
            return count >= _maxFailedAttempts;
        }

        return false;
    }

    public async Task RecordFailedAttemptAsync(
        string ipAddress,
        string normalizedEmail,
        string? tenantSlug,
        CancellationToken ct = default)
    {
        var db = await _redisProvider.GetRequiredDatabaseAsync(ct);
        var key = AuthRedisKeyBuilder.LoginRateLimit(_environment.EnvironmentName, ipAddress, normalizedEmail, tenantSlug);
        var ttlSeconds = (int)Math.Max(1, _window.TotalSeconds);

        await db.ScriptEvaluateAsync(
            RecordAttemptLuaScript,
            new RedisKey[] { key },
            new RedisValue[] { ttlSeconds });
    }

    public async Task ResetAttemptsAsync(
        string ipAddress,
        string normalizedEmail,
        string? tenantSlug,
        CancellationToken ct = default)
    {
        var db = await _redisProvider.GetRequiredDatabaseAsync(ct);
        var key = AuthRedisKeyBuilder.LoginRateLimit(_environment.EnvironmentName, ipAddress, normalizedEmail, tenantSlug);
        await db.KeyDeleteAsync(key);
    }
}
