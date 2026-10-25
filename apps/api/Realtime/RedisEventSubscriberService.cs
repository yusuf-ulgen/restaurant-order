using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Infrastructure.Events;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;

namespace RestaurantOrder.Api.Realtime;

/// <summary>
/// Subscribes to Redis events broadcast channel and bridges them to SignalR hub groups.
/// Enforces single and clear publication model:
/// Uses an atomic Redis lock per EventId to guarantee that only ONE API instance across the cluster
/// bridges a given event into the SignalR Redis backplane.
/// Eliminates duplicate delivery caused by multiple API replicas receiving the same Redis pub/sub message.
/// Push notification delivery is decoupled and handled by the persistent outbox pipeline.
/// </summary>
public sealed class RedisEventSubscriberService : BackgroundService
{
    private static readonly TimeSpan BridgeDeduplicationTtl = TimeSpan.FromMinutes(10);

    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly IHubContext<EventsHub> _hubContext;
    private readonly IRealtimeConnectionTracker _tracker;
    private readonly ILogger<RedisEventSubscriberService> _logger;

    public RedisEventSubscriberService(
        IRedisDatabaseProvider redisProvider,
        IHubContext<EventsHub> hubContext,
        IRealtimeConnectionTracker tracker,
        ILogger<RedisEventSubscriberService> logger)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting RedisEventSubscriberService background bridge...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var subscriber = await _redisProvider.GetSubscriberAsync(stoppingToken);
                if (subscriber == null)
                {
                    await Task.Delay(3000, stoppingToken);
                    continue;
                }

                await subscriber.SubscribeAsync(
                    RedisChannel.Literal(RedisRealtimeEventPublisher.BroadcastChannel),
                    async (channel, message) => await HandleBroadcastMessageAsync(message));

                await subscriber.SubscribeAsync(
                    RedisChannel.Literal(RedisRealtimeEventPublisher.SessionEvictionChannel),
                    async (channel, message) => await HandleSessionEvictionMessageAsync(message));

                _logger.LogInformation("Subscribed to Redis channels '{Broadcast}' and '{Evict}'.",
                    RedisRealtimeEventPublisher.BroadcastChannel,
                    RedisRealtimeEventPublisher.SessionEvictionChannel);

                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis subscriber disconnected or encountered an error. Reconnecting in 3s...");
                await Task.Delay(3000, stoppingToken);
            }
        }

        _logger.LogInformation("RedisEventSubscriberService background bridge stopped.");
    }

    private async Task HandleBroadcastMessageAsync(RedisValue message)
    {
        if (message.IsNullOrEmpty) return;

        try
        {
            using var doc = JsonDocument.Parse(message.ToString());
            var root = doc.RootElement;

            if (!root.TryGetProperty("tenant_id", out var tenantProp) ||
                !root.TryGetProperty("branch_id", out var branchProp) ||
                !root.TryGetProperty("event_type", out var typeProp))
            {
                return;
            }

            var tenantId = tenantProp.GetGuid();
            var branchId = branchProp.GetGuid();
            var eventType = typeProp.GetString();
            if (string.IsNullOrWhiteSpace(eventType)) return;

            var eventId = Guid.Empty;
            if (root.TryGetProperty("event_id", out var evProp) && evProp.TryGetGuid(out var parsedEvId))
            {
                eventId = parsedEvId;
            }

            // Single and clear publication model:
            // Atomically claim the bridge role for this event across all API instances.
            // If another instance already claimed it, skip bridging to avoid duplicate messages in the SignalR backplane.
            if (eventId != Guid.Empty)
            {
                var db = await _redisProvider.GetDatabaseAsync();
                if (db != null)
                {
                    var bridgeKey = $"restaurant-order:realtime:bridged:{eventId:N}";
                    var claimed = await db.StringSetAsync(bridgeKey, Environment.MachineName, BridgeDeduplicationTtl, When.NotExists);
                    if (!claimed)
                    {
                        _logger.LogDebug(
                            "Event '{EventId}' already bridged to SignalR backplane by another API instance. Skipping duplicate bridge.",
                            eventId);
                        return;
                    }
                }
            }

            var floorGroup = $"tenant:{tenantId}:branch:{branchId}:floor";
            var catalogGroup = $"tenant:{tenantId}:branch:{branchId}:catalog";
            var rawEnvelopeJson = message.ToString();

            // Extract optional dining_session_id from data
            Guid? diningSessionId = null;
            if (root.TryGetProperty("data", out var dataProp))
            {
                if (dataProp.TryGetProperty("dining_session_id", out var dsProp) && dsProp.TryGetGuid(out var parsedSessionId))
                {
                    diningSessionId = parsedSessionId;
                }
            }

            switch (eventType)
            {
                case RealtimeEventTypes.OrderCreated:
                case RealtimeEventTypes.OrderItemStatusChanged:
                case RealtimeEventTypes.TableServiceRequested:
                case RealtimeEventTypes.TableServiceRequestStatusChanged:
                    if (diningSessionId.HasValue)
                    {
                        var sessionGroup = $"tenant:{tenantId}:branch:{branchId}:session:{diningSessionId.Value}";
                        await _hubContext.Clients.Group(sessionGroup).SendAsync("ReceiveEvent", rawEnvelopeJson);
                    }
                    await _hubContext.Clients.Group(floorGroup).SendAsync("ReceiveEvent", rawEnvelopeJson);
                    break;

                case RealtimeEventTypes.TableSessionClosed:
                    if (diningSessionId.HasValue)
                    {
                        var sessionGroup = $"tenant:{tenantId}:branch:{branchId}:session:{diningSessionId.Value}";
                        await _hubContext.Clients.Group(sessionGroup).SendAsync("ReceiveEvent", rawEnvelopeJson);
                    }
                    await _hubContext.Clients.Group(floorGroup).SendAsync("ReceiveEvent", rawEnvelopeJson);

                    if (diningSessionId.HasValue)
                    {
                        EvictConnectionsForSession(tenantId, branchId, diningSessionId.Value);
                    }
                    break;

                case RealtimeEventTypes.MenuItemAvailabilityChanged:
                case RealtimeEventTypes.CatalogPolicyInvalidated:
                    await _hubContext.Clients.Group(catalogGroup).SendAsync("ReceiveEvent", rawEnvelopeJson);
                    await _hubContext.Clients.Group(floorGroup).SendAsync("ReceiveEvent", rawEnvelopeJson);
                    break;

                default:
                    await _hubContext.Clients.Group(floorGroup).SendAsync("ReceiveEvent", rawEnvelopeJson);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route Redis broadcast message to SignalR groups.");
        }
    }

    private Task HandleSessionEvictionMessageAsync(RedisValue message)
    {
        if (message.IsNullOrEmpty) return Task.CompletedTask;

        try
        {
            using var doc = JsonDocument.Parse(message.ToString());
            var root = doc.RootElement;
            var tenantId = root.GetProperty("tenant_id").GetGuid();
            var branchId = root.GetProperty("branch_id").GetGuid();
            var sessionId = root.GetProperty("session_id").GetGuid();

            EvictConnectionsForSession(tenantId, branchId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process session eviction control message.");
        }

        return Task.CompletedTask;
    }

    private void EvictConnectionsForSession(Guid tenantId, Guid branchId, Guid sessionId)
    {
        var activeConnections = _tracker.GetConnectionsForSession(tenantId, branchId, sessionId);
        foreach (var conn in activeConnections)
        {
            try
            {
                _hubContext.Clients.Client(conn.ConnectionId).SendAsync("SessionClosed", new { session_id = sessionId });
                _tracker.Untrack(conn.ConnectionId);
                _logger.LogInformation("Evicted connection '{ConnectionId}' following session '{SessionId}' closure.", conn.ConnectionId, sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanly evict connection '{ConnectionId}'.", conn.ConnectionId);
            }
        }
    }
}
