using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;

namespace RestaurantOrder.Infrastructure.Events;

public sealed class RedisRealtimeEventPublisher : IRealtimeEventPublisher
{
    public const string BroadcastChannel = "restaurant-order:events:broadcast";
    public const string SessionEvictionChannel = "restaurant-order:sessions:evict";

    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly ILogger<RedisRealtimeEventPublisher> _logger;

    public RedisRealtimeEventPublisher(
        IRedisDatabaseProvider redisProvider,
        ILogger<RedisRealtimeEventPublisher> logger)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishBroadcastAsync(string eventPayloadJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventPayloadJson);

        try
        {
            var subscriber = await _redisProvider.GetRequiredSubscriberAsync(ct);
            await subscriber.PublishAsync(RedisChannel.Literal(BroadcastChannel), eventPayloadJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish operational event to Redis broadcast channel '{Channel}'.", BroadcastChannel);
            throw;
        }
    }

    public async Task PublishSessionInvalidatedAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            var subscriber = await _redisProvider.GetRequiredSubscriberAsync(ct);
            var payload = JsonSerializer.Serialize(new
            {
                tenant_id = tenantId,
                branch_id = branchId,
                session_id = sessionId,
                invalidated_at_utc = DateTimeOffset.UtcNow
            });

            await subscriber.PublishAsync(RedisChannel.Literal(SessionEvictionChannel), payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish session eviction event for Session '{SessionId}'.", sessionId);
            throw;
        }
    }
}
