using Microsoft.Extensions.Hosting;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;

namespace RestaurantOrder.Infrastructure.Floor;

/// <summary>
/// Distributed Redis-backed rate limiter for public QR resolve and exchange endpoints.
/// Strictly enforces fail-closed behavior on Redis failure to prevent abuse.
/// Raw QR tokens are never included in rate-limit keys or error messages.
/// </summary>
public sealed class RedisQrRateLimiter : IQrRateLimiter
{
    private const string RateLimitLuaScript = @"
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return current";

    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly IHostEnvironment _environment;
    private readonly int _maxResolveAttempts;
    private readonly int _maxExchangeAttempts;
    private readonly TimeSpan _window;

    public RedisQrRateLimiter(
        IRedisDatabaseProvider redisProvider,
        IHostEnvironment environment,
        int maxResolveAttempts = 60,
        int maxExchangeAttempts = 15,
        TimeSpan? window = null)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _maxResolveAttempts = maxResolveAttempts;
        _maxExchangeAttempts = maxExchangeAttempts;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    public async Task EnsureNotRateLimitedAsync(string clientIp, string action, CancellationToken ct = default)
    {
        var ip = string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp.Trim();
        var act = string.IsNullOrWhiteSpace(action) ? "default" : action.Trim().ToLowerInvariant();
        var key = $"qr:rl:{_environment.EnvironmentName}:{ip}:{act}";
        var ttlSeconds = (int)Math.Max(1, _window.TotalSeconds);

        var limit = act switch
        {
            "exchange" => _maxExchangeAttempts,
            _ => _maxResolveAttempts
        };

        IDatabase db;
        try
        {
            db = await _redisProvider.GetRequiredDatabaseAsync(ct);
        }
        catch (Exception ex)
        {
            throw new DistributedSecurityStateUnavailableException("Distributed QR rate limiter is unavailable. Access denied.", ex);
        }

        RedisResult result;
        try
        {
            result = await db.ScriptEvaluateAsync(
                RateLimitLuaScript,
                new RedisKey[] { key },
                new RedisValue[] { ttlSeconds });
        }
        catch (Exception ex)
        {
            throw new DistributedSecurityStateUnavailableException("Rate limit evaluation failed.", ex);
        }

        var currentAttempts = (long)result;
        if (currentAttempts > limit)
        {
            throw new QrRateLimitException(ttlSeconds, $"Rate limit exceeded for action '{act}'. Try again later.");
        }
    }
}
