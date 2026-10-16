namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Domain-level exception thrown when a customer exceeds service request frequency limits.
/// </summary>
public class ServiceRequestRateLimitException : Exception
{
    public int RetryAfterSeconds { get; }

    public ServiceRequestRateLimitException(int retryAfterSeconds, string message)
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}

/// <summary>
/// Rate limiter to protect staff and backend against spam or rapid-fire service requests.
/// </summary>
public interface IServiceRequestRateLimiter
{
    Task EnsureNotRateLimitedAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);
}
