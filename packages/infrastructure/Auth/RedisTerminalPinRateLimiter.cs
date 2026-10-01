using Microsoft.Extensions.Hosting;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Distributed Redis-backed rate limiter for terminal PIN authentication.
/// Enforces progressive backoff delays and hard lockouts with strict fail-closed guarantees.
/// </summary>
public sealed class RedisTerminalPinRateLimiter : ITerminalPinRateLimiter
{
    private const string RecordAttemptLuaScript = @"
        local attempts = redis.call('INCR', KEYS[1])
        if attempts == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        if attempts >= tonumber(ARGV[2]) then
            redis.call('SET', KEYS[2], '1', 'EX', ARGV[3])
        end
        return attempts";

    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly IHostEnvironment _environment;
    private readonly TimeSpan _windowDuration = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _lockoutDuration = TimeSpan.FromMinutes(15);
    private const int BackoffThreshold = 3;
    private const int LockoutThreshold = 5;

    public RedisTerminalPinRateLimiter(
        IRedisDatabaseProvider redisProvider,
        IHostEnvironment environment)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task<PinRateLimitResult> CheckAttemptAllowedAsync(
        Guid terminalId,
        Guid? userId,
        CancellationToken ct = default)
    {
        var db = await _redisProvider.GetRequiredDatabaseAsync(ct);
        var attemptsKey = AuthRedisKeyBuilder.TerminalPinAttempts(_environment.EnvironmentName, terminalId, userId);
        var lockedKey = AuthRedisKeyBuilder.TerminalPinLocked(_environment.EnvironmentName, terminalId, userId);

        // Check if hard lockout is active
        var lockedTtl = await db.KeyTimeToLiveAsync(lockedKey);
        if (lockedTtl.HasValue && lockedTtl.Value > TimeSpan.Zero)
        {
            return PinRateLimitResult.LockedOut(lockedTtl.Value);
        }

        // Check failure attempts for progressive backoff
        var attemptsVal = await db.StringGetAsync(attemptsKey);
        if (!attemptsVal.IsNullOrEmpty && int.TryParse((string?)attemptsVal, out var count))
        {
            if (count >= BackoffThreshold)
            {
                var delaySeconds = (count - BackoffThreshold + 1) * 2;
                return PinRateLimitResult.DelayRequired(TimeSpan.FromSeconds(delaySeconds));
            }
        }

        return PinRateLimitResult.Allowed();
    }

    public async Task RecordFailedAttemptAsync(
        Guid terminalId,
        Guid? userId,
        CancellationToken ct = default)
    {
        var db = await _redisProvider.GetRequiredDatabaseAsync(ct);
        var attemptsKey = AuthRedisKeyBuilder.TerminalPinAttempts(_environment.EnvironmentName, terminalId, userId);
        var lockedKey = AuthRedisKeyBuilder.TerminalPinLocked(_environment.EnvironmentName, terminalId, userId);

        var windowSeconds = (int)_windowDuration.TotalSeconds;
        var lockoutSeconds = (int)_lockoutDuration.TotalSeconds;

        await db.ScriptEvaluateAsync(
            RecordAttemptLuaScript,
            new RedisKey[] { attemptsKey, lockedKey },
            new RedisValue[] { windowSeconds, LockoutThreshold, lockoutSeconds });
    }

    public async Task ResetAttemptsAsync(
        Guid terminalId,
        Guid? userId,
        CancellationToken ct = default)
    {
        var db = await _redisProvider.GetRequiredDatabaseAsync(ct);
        var attemptsKey = AuthRedisKeyBuilder.TerminalPinAttempts(_environment.EnvironmentName, terminalId, userId);
        var lockedKey = AuthRedisKeyBuilder.TerminalPinLocked(_environment.EnvironmentName, terminalId, userId);

        await db.KeyDeleteAsync(new RedisKey[] { attemptsKey, lockedKey });
    }
}
