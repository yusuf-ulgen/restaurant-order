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

using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Api.Realtime;

public sealed class RedisEventSubscriberService : BackgroundService
{
    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly IHubContext<EventsHub> _hubContext;
    private readonly IRealtimeConnectionTracker _tracker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPushNotificationTransport _pushTransport;
    private readonly ILogger<RedisEventSubscriberService> _logger;

    public RedisEventSubscriberService(
        IRedisDatabaseProvider redisProvider,
        IHubContext<EventsHub> hubContext,
        IRealtimeConnectionTracker tracker,
        IServiceScopeFactory scopeFactory,
        IPushNotificationTransport pushTransport,
        ILogger<RedisEventSubscriberService> logger)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _pushTransport = pushTransport ?? throw new ArgumentNullException(nameof(pushTransport));
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

                // Wait until cancellation
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

                    // Continuous auth: evict connections for closed session
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

            var eventId = Guid.NewGuid();
            if (root.TryGetProperty("event_id", out var evProp) && evProp.TryGetGuid(out var parsedEvId))
            {
                eventId = parsedEvId;
            }

            // Asynchronously dispatch push notification to background devices
            _ = DispatchPushNotificationsAsync(tenantId, branchId, eventType, eventId, diningSessionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route Redis broadcast message to SignalR groups.");
        }
    }

    private async Task DispatchPushNotificationsAsync(
        Guid tenantId,
        Guid branchId,
        string eventType,
        Guid eventId,
        Guid? diningSessionId,
        CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RestaurantOrderDbContext>();
            var now = DateTimeOffset.UtcNow;
            var tenantIdTyped = new TenantId(tenantId);
            var branchIdTyped = new BranchId(branchId);

            IReadOnlyList<DeviceRegistration> targets;

            if (eventType == RealtimeEventTypes.OrderItemStatusChanged && diningSessionId.HasValue)
            {
                var sessionTyped = new DiningSessionId(diningSessionId.Value);
                targets = await db.DeviceRegistrations
                    .IgnoreQueryFilters()
                    .Where(d => d.TenantId == tenantIdTyped && d.DiningSessionId == sessionTyped && d.IsActive && d.ExpiresAtUtc > now)
                    .ToListAsync(ct);
            }
            else if (eventType == RealtimeEventTypes.OrderCreated || eventType == RealtimeEventTypes.TableServiceRequested)
            {
                targets = await db.DeviceRegistrations
                    .IgnoreQueryFilters()
                    .Where(d => d.TenantId == tenantIdTyped && d.BranchId == branchIdTyped && d.UserId != null && d.IsActive && d.ExpiresAtUtc > now)
                    .ToListAsync(ct);
            }
            else
            {
                return;
            }

            var deactivatedAny = false;
            foreach (var dev in targets)
            {
                try
                {
                    var pushRequest = new PushNotificationRequest(dev.InstallationId, eventId, dev.ExpiresAtUtc);
                    var result = await _pushTransport.SendAsync(pushRequest, ct);

                    if (result.Status == PushSendStatus.InvalidRecipient)
                    {
                        dev.Deactivate(now);
                        deactivatedAny = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Push delivery attempt failed for device installation {InstallationId}.", dev.InstallationId);
                }
            }

            if (deactivatedAny)
            {
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch push notifications for event {EventType} {EventId}.", eventType, eventId);
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
