namespace RestaurantOrder.Application.Auth;

public sealed record PinRateLimitResult(
    bool IsAllowed,
    TimeSpan? BackoffDelay,
    int RetryAfterSeconds,
    bool IsLockedOut,
    string? Message)
{
    public static PinRateLimitResult Allowed() =>
        new(IsAllowed: true, BackoffDelay: null, RetryAfterSeconds: 0, IsLockedOut: false, Message: null);

    public static PinRateLimitResult DelayRequired(TimeSpan delay) =>
        new(IsAllowed: true, BackoffDelay: delay, RetryAfterSeconds: (int)Math.Ceiling(delay.TotalSeconds), IsLockedOut: false, Message: "Progressive backoff active.");

    public static PinRateLimitResult LockedOut(TimeSpan remainingLockout) =>
        new(IsAllowed: false, BackoffDelay: null, RetryAfterSeconds: (int)Math.Ceiling(remainingLockout.TotalSeconds), IsLockedOut: true, Message: "Terminal temporarily locked due to excessive failed attempts.");
}

/// <summary>
/// Enforces progressive backoff delays and hard lockouts for staff PIN attempts
/// scoped per terminal and optionally per staff identity.
/// </summary>
public interface ITerminalPinRateLimiter
{
    Task<PinRateLimitResult> CheckAttemptAllowedAsync(
        Guid terminalId,
        Guid? userId,
        CancellationToken ct = default);

    Task RecordFailedAttemptAsync(
        Guid terminalId,
        Guid? userId,
        CancellationToken ct = default);

    Task ResetAttemptsAsync(
        Guid terminalId,
        Guid? userId,
        CancellationToken ct = default);
}
