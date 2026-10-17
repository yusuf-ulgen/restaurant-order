using Microsoft.Extensions.Hosting;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;

namespace RestaurantOrder.Infrastructure.Floor;

/// <summary>
/// Distributed Redis-backed rate limiter for customer table service requests.
/// Enforces per-dining-session cooldown and sliding-window request limits atomically via Lua script.
/// Strictly enforces fail-closed behavior in Staging/Production if Redis is unavailable.
/// </summary>
public sealed class RedisServiceRequestRateLimiter : IServiceRequestRateLimiter
{
    private const string RateLimitLuaScript = @"
        local zset_key = KEYS[1]
        local last_key = KEYS[2]
        local seq_key = KEYS[1] .. ':seq'
        local now = tonumber(ARGV[1])
        local cooldown = tonumber(ARGV[2])
        local window = tonumber(ARGV[3])
        local max_req = tonumber(ARGV[4])
        local ttl = tonumber(ARGV[5])

        -- 1. Check cooldown between consecutive requests
        local last_ts = redis.call('GET', last_key)
        if last_ts then
            local elapsed = now - tonumber(last_ts)
            if elapsed < cooldown then
                local retry_after = math.ceil(cooldown - elapsed)
                if retry_after < 1 then retry_after = 1 end
                return { 0, retry_after, 'cooldown' }
            end
        end

        -- 2. Prune entries outside sliding window
        local cutoff = now - window
        redis.call('ZREMRANGEBYSCORE', zset_key, '-inf', cutoff)

        -- 3. Check sliding window limit
        local current_count = redis.call('ZCARD', zset_key)
        if current_count >= max_req then
            local oldest = redis.call('ZRANGE', zset_key, 0, 0, 'WITHSCORES')
            local oldest_ts = now
            if oldest and #oldest >= 2 then
                oldest_ts = tonumber(oldest[2])
            end
            local remaining = window - (now - oldest_ts)
            local retry_after = math.ceil(remaining)
            if retry_after < 1 then retry_after = 1 end
            return { 0, retry_after, 'window' }
        end

        -- 4. Record new request timestamp
        local seq = redis.call('INCR', seq_key)
        local member = tostring(now) .. '-' .. tostring(seq)
        redis.call('ZADD', zset_key, now, member)
        redis.call('SET', last_key, now, 'EX', ttl)
        redis.call('EXPIRE', zset_key, ttl)
        redis.call('EXPIRE', seq_key, ttl)

        return { 1, 0, 'ok' }";

    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly IHostEnvironment _environment;
    private readonly TimeSpan _cooldownPeriod;
    private readonly TimeSpan _windowPeriod;
    private readonly int _maxRequestsPerWindow;

    public RedisServiceRequestRateLimiter(
        IRedisDatabaseProvider redisProvider,
        IHostEnvironment environment,
        TimeSpan? cooldownPeriod = null,
        TimeSpan? windowPeriod = null,
        int maxRequestsPerWindow = 5)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _cooldownPeriod = cooldownPeriod ?? TimeSpan.FromSeconds(15);
        _windowPeriod = windowPeriod ?? TimeSpan.FromMinutes(2);
        _maxRequestsPerWindow = maxRequestsPerWindow;
    }

    public async Task EnsureNotRateLimitedAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default)
    {
        var key = $"sr:rl:{_environment.EnvironmentName}:{tenantId:N}:{sessionId:N}";
        var lastKey = $"sr:rl-last:{_environment.EnvironmentName}:{tenantId:N}:{sessionId:N}";
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cooldownSeconds = (int)Math.Max(1, _cooldownPeriod.TotalSeconds);
        var windowSeconds = (int)Math.Max(1, _windowPeriod.TotalSeconds);
        var ttlSeconds = (int)Math.Max(windowSeconds * 2, 300);

        IDatabase db;
        try
        {
            var maybeDb = await _redisProvider.GetDatabaseAsync(ct);
            if (maybeDb == null)
            {
                if (_environment.IsDevelopment())
                {
                    return;
                }
                throw new DistributedSecurityStateUnavailableException("Distributed Redis security infrastructure is unavailable.");
            }
            db = maybeDb;
        }
        catch (Exception ex)
        {
            if (_environment.IsDevelopment())
            {
                return;
            }
            throw new DistributedSecurityStateUnavailableException("Distributed service request rate limiter is unavailable. Access denied.", ex);
        }

        RedisResult result;
        try
        {
            result = await db.ScriptEvaluateAsync(
                RateLimitLuaScript,
                new RedisKey[] { key, lastKey },
                new RedisValue[] { nowSeconds, cooldownSeconds, windowSeconds, _maxRequestsPerWindow, ttlSeconds });
        }
        catch (Exception ex)
        {
            if (_environment.IsDevelopment())
            {
                return;
            }
            throw new DistributedSecurityStateUnavailableException("Rate limit evaluation failed.", ex);
        }

        if (result.IsNull)
        {
            return;
        }

        var results = (RedisResult[])result!;
        var allowed = (int)results[0] == 1;
        var retryAfter = (int)results[1];
        var reason = (string)results[2]!;

        if (!allowed)
        {
            var message = reason == "cooldown"
                ? $"Please wait {retryAfter} seconds before submitting another service request."
                : $"Service request rate limit exceeded. Please wait {retryAfter} seconds before requesting again.";

            throw new ServiceRequestRateLimitException(retryAfter, message);
        }
    }

    public void Reset(Guid tenantId, Guid sessionId)
    {
        try
        {
            var db = _redisProvider.GetDatabaseAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (db != null)
            {
                var key = $"sr:rl:{_environment.EnvironmentName}:{tenantId:N}:{sessionId:N}";
                var lastKey = $"sr:rl-last:{_environment.EnvironmentName}:{tenantId:N}:{sessionId:N}";
                var seqKey = $"{key}:seq";
                db.KeyDelete(new RedisKey[] { key, lastKey, seqKey });
            }
        }
        catch
        {
            // Suppress reset errors in background
        }
    }
}
