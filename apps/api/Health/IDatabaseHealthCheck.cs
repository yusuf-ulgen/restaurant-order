namespace RestaurantOrder.Api.Health;

/// <summary>
/// Abstraction for verifying PostgreSQL database connectivity and readiness.
/// </summary>
public interface IDatabaseHealthCheck
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
