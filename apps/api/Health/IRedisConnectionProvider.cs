using StackExchange.Redis;

namespace RestaurantOrder.Api.Health;

/// <summary>
/// Manages singleton lifecycle and connection reuse for StackExchange.Redis ConnectionMultiplexer.
/// Ensures single connection instance across health probes and graceful disposal on shutdown.
/// </summary>
public interface IRedisConnectionProvider : IAsyncDisposable, IDisposable
{
    Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken cancellationToken = default);
}
