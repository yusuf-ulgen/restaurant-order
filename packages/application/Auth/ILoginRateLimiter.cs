namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Composite rate limiter protecting the login endpoint across IP, account email, and tenant context.
/// Prevents distributed credential stuffing and targeted brute-force attacks.
/// </summary>
public interface ILoginRateLimiter
{
    Task<bool> IsRateLimitedAsync(string ipAddress, string normalizedEmail, string? tenantSlug, CancellationToken ct = default);

    Task RecordFailedAttemptAsync(string ipAddress, string normalizedEmail, string? tenantSlug, CancellationToken ct = default);

    Task ResetAttemptsAsync(string ipAddress, string normalizedEmail, string? tenantSlug, CancellationToken ct = default);
}
