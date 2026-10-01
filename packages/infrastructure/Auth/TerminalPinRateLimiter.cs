using System.Collections.Concurrent;
using RestaurantOrder.Application.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Thread-safe in-memory rate limiter enforcing progressive backoff delays
/// and hard lockouts for staff PIN attempts scoped to a physical terminal.
/// </summary>
public sealed class TerminalPinRateLimiter : ITerminalPinRateLimiter
{
    private sealed record AttemptRecord(
        int FailureCount,
        DateTimeOffset LastFailureUtc,
        DateTimeOffset? LockedUntilUtc);

    private readonly ConcurrentDictionary<string, AttemptRecord> _records = new(StringComparer.Ordinal);
    private readonly TimeSpan _windowDuration = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _lockoutDuration = TimeSpan.FromMinutes(15);
    private const int BackoffThreshold = 3;
    private const int LockoutThreshold = 5;

    public Task<PinRateLimitResult> CheckAttemptAllowedAsync(
        Guid terminalId,
        Guid? userId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var key = BuildKey(terminalId, userId);

        if (_records.TryGetValue(key, out var record))
        {
            // Check if active lockout exists
            if (record.LockedUntilUtc.HasValue && record.LockedUntilUtc.Value > now)
            {
                var remaining = record.LockedUntilUtc.Value - now;
                return Task.FromResult(PinRateLimitResult.LockedOut(remaining));
            }

            // Window expiry check
            if (now - record.LastFailureUtc > _windowDuration && (!record.LockedUntilUtc.HasValue || record.LockedUntilUtc.Value <= now))
            {
                _records.TryRemove(key, out _);
                return Task.FromResult(PinRateLimitResult.Allowed());
            }

            // Progressive backoff check
            if (record.FailureCount >= BackoffThreshold)
            {
                var delaySeconds = (record.FailureCount - BackoffThreshold + 1) * 2;
                var delay = TimeSpan.FromSeconds(delaySeconds);
                return Task.FromResult(PinRateLimitResult.DelayRequired(delay));
            }
        }

        return Task.FromResult(PinRateLimitResult.Allowed());
    }

    public Task RecordFailedAttemptAsync(
        Guid terminalId,
        Guid? userId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var key = BuildKey(terminalId, userId);

        _records.AddOrUpdate(
            key,
            _ => new AttemptRecord(1, now, null),
            (_, existing) =>
            {
                // If previous failures were expired, start fresh
                var isExpired = (now - existing.LastFailureUtc) > _windowDuration;
                var count = isExpired ? 1 : existing.FailureCount + 1;
                DateTimeOffset? lockedUntil = count >= LockoutThreshold ? now.Add(_lockoutDuration) : null;
                return new AttemptRecord(count, now, lockedUntil);
            });

        return Task.CompletedTask;
    }

    public Task ResetAttemptsAsync(
        Guid terminalId,
        Guid? userId,
        CancellationToken ct = default)
    {
        var key = BuildKey(terminalId, userId);
        _records.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private static string BuildKey(Guid terminalId, Guid? userId) =>
        userId.HasValue ? $"t:{terminalId:N}:u:{userId.Value:N}" : $"t:{terminalId:N}";
}
