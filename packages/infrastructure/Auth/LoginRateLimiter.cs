using System.Collections.Concurrent;
using RestaurantOrder.Application.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// In-memory sliding window composite rate limiter for authentication attempts.
/// Tracks failed attempts per IP + normalized email + tenant slug.
/// </summary>
public sealed class LoginRateLimiter : ILoginRateLimiter
{
    private sealed class AttemptTracker
    {
        public readonly List<DateTimeOffset> Failures = new();
        public readonly object Lock = new();
    }

    private readonly ConcurrentDictionary<string, AttemptTracker> _trackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxFailedAttempts;
    private readonly TimeSpan _window;

    public LoginRateLimiter(int maxFailedAttempts = 5, TimeSpan? window = null)
    {
        _maxFailedAttempts = maxFailedAttempts;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    private static string BuildKey(string ipAddress, string normalizedEmail, string? tenantSlug)
    {
        var ip = string.IsNullOrWhiteSpace(ipAddress) ? "unknown-ip" : ipAddress.Trim();
        var email = string.IsNullOrWhiteSpace(normalizedEmail) ? "unknown-email" : normalizedEmail.Trim().ToLowerInvariant();
        var slug = string.IsNullOrWhiteSpace(tenantSlug) ? "global" : tenantSlug.Trim().ToLowerInvariant();
        return $"{ip}:{email}:{slug}";
    }

    public Task<bool> IsRateLimitedAsync(string ipAddress, string normalizedEmail, string? tenantSlug, CancellationToken ct = default)
    {
        var key = BuildKey(ipAddress, normalizedEmail, tenantSlug);
        if (!_trackers.TryGetValue(key, out var tracker))
        {
            return Task.FromResult(false);
        }

        var cutoff = DateTimeOffset.UtcNow - _window;
        lock (tracker.Lock)
        {
            tracker.Failures.RemoveAll(t => t < cutoff);
            return Task.FromResult(tracker.Failures.Count >= _maxFailedAttempts);
        }
    }

    public Task RecordFailedAttemptAsync(string ipAddress, string normalizedEmail, string? tenantSlug, CancellationToken ct = default)
    {
        var key = BuildKey(ipAddress, normalizedEmail, tenantSlug);
        var tracker = _trackers.GetOrAdd(key, _ => new AttemptTracker());

        var now = DateTimeOffset.UtcNow;
        var cutoff = now - _window;
        lock (tracker.Lock)
        {
            tracker.Failures.RemoveAll(t => t < cutoff);
            tracker.Failures.Add(now);
        }

        return Task.CompletedTask;
    }

    public Task ResetAttemptsAsync(string ipAddress, string normalizedEmail, string? tenantSlug, CancellationToken ct = default)
    {
        var key = BuildKey(ipAddress, normalizedEmail, tenantSlug);
        _trackers.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
