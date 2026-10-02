using StackExchange.Redis;

namespace RestaurantOrder.Infrastructure.Redis;

/// <summary>
/// Provides access to the distributed Redis database instance for stateful security components.
/// Enforces fail-closed semantics when Redis connectivity is absent or broken.
/// </summary>
public interface IRedisDatabaseProvider
{
    Task<IDatabase?> GetDatabaseAsync(CancellationToken ct = default);
    Task<IDatabase> GetRequiredDatabaseAsync(CancellationToken ct = default);
}
