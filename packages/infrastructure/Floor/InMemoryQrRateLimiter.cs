using System.Collections.Concurrent;
using RestaurantOrder.Application.Floor;

namespace RestaurantOrder.Infrastructure.Floor;

/// <summary>
/// In-memory sliding window rate limiter for public QR endpoints (used in unit tests and local development).
/// </summary>
public sealed class InMemoryQrRateLimiter : IQrRateLimiter
{
    private sealed class AttemptTracker
    {
        public readonly List<DateTimeOffset> Attempts = new();
        public readonly object Lock = new();
    }

    private readonly ConcurrentDictionary<string, AttemptTracker> _trackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxResolveAttempts;
    private readonly int _maxExchangeAttempts;
    private readonly TimeSpan _window;

    public InMemoryQrRateLimiter(
        int maxResolveAttempts = 60,
        int maxExchangeAttempts = 15,
        TimeSpan? window = null)
    {
        _maxResolveAttempts = maxResolveAttempts;
        _maxExchangeAttempts = maxExchangeAttempts;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    public Task EnsureNotRateLimitedAsync(string clientIp, string action, CancellationToken ct = default)
    {
        var ip = string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp.Trim();
        var act = string.IsNullOrWhiteSpace(action) ? "default" : action.Trim().ToLowerInvariant();
        var key = $"{ip}:{act}";
        var limit = act switch
        {
            "exchange" => _maxExchangeAttempts,
            _ => _maxResolveAttempts
        };

        var tracker = _trackers.GetOrAdd(key, _ => new AttemptTracker());
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - _window;

        lock (tracker.Lock)
        {
            tracker.Attempts.RemoveAll(t => t < cutoff);
            tracker.Attempts.Add(now);

            if (tracker.Attempts.Count > limit)
            {
                var retryAfter = (int)Math.Max(1, _window.TotalSeconds);
                throw new QrRateLimitException(retryAfter, $"Rate limit exceeded for action '{act}'. Try again later.");
            }
        }

        return Task.CompletedTask;
    }
}
