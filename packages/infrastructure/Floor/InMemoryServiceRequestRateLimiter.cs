using System.Collections.Concurrent;
using RestaurantOrder.Application.Floor;

namespace RestaurantOrder.Infrastructure.Floor;

/// <summary>
/// Thread-safe in-memory sliding window and cooldown rate limiter for customer service requests.
/// Prevents spamming staff with rapid repeated requests from the same dining session.
/// </summary>
public sealed class InMemoryServiceRequestRateLimiter : IServiceRequestRateLimiter
{
    private sealed class SessionRequestTracker
    {
        public readonly List<DateTimeOffset> Timestamps = new();
        public DateTimeOffset LastRequestAtUtc = DateTimeOffset.MinValue;
        public readonly object Lock = new();
    }

    private readonly ConcurrentDictionary<string, SessionRequestTracker> _trackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _cooldownPeriod;
    private readonly TimeSpan _windowPeriod;
    private readonly int _maxRequestsPerWindow;

    public InMemoryServiceRequestRateLimiter(
        TimeSpan? cooldownPeriod = null,
        TimeSpan? windowPeriod = null,
        int maxRequestsPerWindow = 5)
    {
        _cooldownPeriod = cooldownPeriod ?? TimeSpan.FromSeconds(15);
        _windowPeriod = windowPeriod ?? TimeSpan.FromMinutes(2);
        _maxRequestsPerWindow = maxRequestsPerWindow;
    }

    public Task EnsureNotRateLimitedAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default)
    {
        var key = $"{tenantId:N}:{sessionId:N}";
        var tracker = _trackers.GetOrAdd(key, _ => new SessionRequestTracker());
        var now = DateTimeOffset.UtcNow;

        lock (tracker.Lock)
        {
            // 1. Check cooldown between consecutive requests
            if (tracker.LastRequestAtUtc != DateTimeOffset.MinValue)
            {
                var elapsedSinceLast = now - tracker.LastRequestAtUtc;
                if (elapsedSinceLast < _cooldownPeriod)
                {
                    var retryAfterSeconds = (int)Math.Ceiling((_cooldownPeriod - elapsedSinceLast).TotalSeconds);
                    if (retryAfterSeconds < 1) retryAfterSeconds = 1;
                    throw new ServiceRequestRateLimitException(
                        retryAfterSeconds,
                        $"Please wait {retryAfterSeconds} seconds before submitting another service request.");
                }
            }

            // 2. Check sliding window limit
            var windowCutoff = now - _windowPeriod;
            tracker.Timestamps.RemoveAll(ts => ts < windowCutoff);

            if (tracker.Timestamps.Count >= _maxRequestsPerWindow)
            {
                var oldestInWindow = tracker.Timestamps[0];
                var windowRemaining = _windowPeriod - (now - oldestInWindow);
                var retryAfterSeconds = (int)Math.Ceiling(windowRemaining.TotalSeconds);
                if (retryAfterSeconds < 1) retryAfterSeconds = 1;
                throw new ServiceRequestRateLimitException(
                    retryAfterSeconds,
                    $"Service request rate limit exceeded. Please wait {retryAfterSeconds} seconds before requesting again.");
            }

            // Record request
            tracker.LastRequestAtUtc = now;
            tracker.Timestamps.Add(now);
        }

        return Task.CompletedTask;
    }

    public void Reset(Guid tenantId, Guid sessionId)
    {
        var key = $"{tenantId:N}:{sessionId:N}";
        _trackers.TryRemove(key, out _);
    }
}
