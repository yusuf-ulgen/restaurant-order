namespace RestaurantOrder.Api.Health;

/// <summary>
/// Abstraction for verifying Redis cache connectivity and readiness.
/// </summary>
public interface IRedisHealthCheck
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
