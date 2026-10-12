namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Distributed rate limiter for public QR resolve and exchange endpoints.
/// Prevents token enumeration and brute-force scanning.
/// </summary>
public interface IQrRateLimiter
{
    Task EnsureNotRateLimitedAsync(string clientIp, string action, CancellationToken ct = default);
}
