using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
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
/// Her API Redis yayınını yalnızca kendi bağlantılarına, güncel yetki kontrolünden sonra iletir.
/// Aynı yayında birden fazla gruba bağlı alıcı bir kez seçilir. Redis tekrarları istemcide
/// sabit event ID ile tekilleştirilir. Push teslimi ayrı kalıcı kuyrukta işlenir.
/// </summary>
public sealed class RedisEventSubscriberService : BackgroundService
{

    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly IHubContext<EventsHub> _hubContext;
    private readonly IRealtimeConnectionTracker _tracker;
    private readonly ILogger<RedisEventSubscriberService> _logger;
    private readonly IRealtimeConnectionAuthorizer _authorizer;

    public RedisEventSubscriberService(
        IRedisDatabaseProvider redisProvider,
        IHubContext<EventsHub> hubContext,
        IRealtimeConnectionTracker tracker,
        ILogger<RedisEventSubscriberService> logger,
        IRealtimeConnectionAuthorizer authorizer)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authorizer = authorizer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting RedisEventSubscriberService background bridge...");

        // Start periodic token expiration sweep task
        _ = RunPeriodicExpirySweepAsync(stoppingToken);

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
                    RedisChannel.Literal("restaurant-order:events:auth-invalidated"),
                    async (_, message) => await HandleAuthInvalidatedAsync(message));

                await subscriber.SubscribeAsync(
                    RedisChannel.Literal(RedisRealtimeEventPublisher.BroadcastChannel),
                    async (channel, message) => await HandleBroadcastMessageAsync(message));

                await subscriber.SubscribeAsync(
                    RedisChannel.Literal(RedisRealtimeEventPublisher.SessionEvictionChannel),
                    async (channel, message) => await HandleSessionEvictionMessageAsync(message));

                await subscriber.SubscribeAsync(
                    RedisChannel.Literal(RedisRealtimeEventPublisher.UserEvictionChannel),
                    async (channel, message) => await HandleUserEvictionMessageAsync(message));

                _logger.LogInformation("Subscribed to Redis channels '{Broadcast}', '{SessionEvict}', and '{UserEvict}'.",
                    RedisRealtimeEventPublisher.BroadcastChannel,
                    RedisRealtimeEventPublisher.SessionEvictionChannel,
                    RedisRealtimeEventPublisher.UserEvictionChannel);

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

    internal async Task RunPeriodicExpirySweepAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                await SweepExpiredConnectionsAsync(DateTimeOffset.UtcNow);
                foreach (var connection in _tracker.GetAllConnections())
                    if (!await _authorizer.IsAuthorizedAsync(connection, stoppingToken)) await TerminateAsync(connection);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error occurred during periodic connection expiry sweep.");
            }
        }
    }

    internal async Task SweepExpiredConnectionsAsync(DateTimeOffset now)
    {
        var expiredConnections = _tracker.GetExpiredConnections(now);
        foreach (var conn in expiredConnections)
        {
            try
            {
                await _hubContext.Clients.Client(conn.ConnectionId).SendAsync("TokenExpired", new { expired_at_utc = conn.TokenExpiresAtUtc });

                if (conn.SubscribedGroups != null)
                {
                    foreach (var group in conn.SubscribedGroups)
                    {
                        await _hubContext.Groups.RemoveFromGroupAsync(conn.ConnectionId, group);
                    }
                }

                conn.Abort?.Invoke();
                _tracker.Untrack(conn.ConnectionId);

                _logger.LogInformation("Terminated expired SignalR connection '{ConnectionId}' (expired at {ExpiredAtUtc}).",
                    conn.ConnectionId, conn.TokenExpiresAtUtc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evict expired connection '{ConnectionId}'.", conn.ConnectionId);
            }
            finally { conn.Abort?.Invoke(); _tracker.Untrack(conn.ConnectionId); }
        }
    }

    internal async Task HandleBroadcastMessageAsync(RedisValue message)
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

            var floorGroup = $"tenant:{tenantId}:branch:{branchId}:floor";
            var catalogGroup = $"tenant:{tenantId}:branch:{branchId}:catalog";
            var kitchenGroup = $"tenant:{tenantId}:branch:{branchId}:station:kitchen";
            var barGroup = $"tenant:{tenantId}:branch:{branchId}:station:bar";
            var rawEnvelopeJson = message.ToString();
            var delivered = new HashSet<string>();
            async Task SendGroupAsync(string group)
            {
                foreach (var connection in _tracker.GetAllConnections().Where(c => c.SubscribedGroups?.Contains(group) == true))
                {
                    if (!delivered.Add(connection.ConnectionId)) continue;
                    if (!await _authorizer.IsAuthorizedAsync(connection))
                    {
                        await TerminateAsync(connection);
                        continue;
                    }
                    await _hubContext.Clients.Client(connection.ConnectionId).SendAsync("ReceiveEvent", rawEnvelopeJson);
                }
            }

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
                    // Route to floor, station queues, and customer's own session
                    if (diningSessionId.HasValue)
                    {
                        var sessionGroup = $"tenant:{tenantId}:branch:{branchId}:session:{diningSessionId.Value}";
                        await SendGroupAsync(sessionGroup);
                    }
                    await SendGroupAsync(floorGroup);
                    await SendGroupAsync(kitchenGroup);
                    await SendGroupAsync(barGroup);
                    break;

                case RealtimeEventTypes.TableServiceRequested:
                case RealtimeEventTypes.TableServiceRequestStatusChanged:
                    if (diningSessionId.HasValue)
                    {
                        var sessionGroup = $"tenant:{tenantId}:branch:{branchId}:session:{diningSessionId.Value}";
                        await SendGroupAsync(sessionGroup);
                    }
                    await SendGroupAsync(floorGroup);
                    break;

                case RealtimeEventTypes.TableSessionClosed:
                    if (diningSessionId.HasValue)
                    {
                        var sessionGroup = $"tenant:{tenantId}:branch:{branchId}:session:{diningSessionId.Value}";
                        await SendGroupAsync(sessionGroup);
                    }
                    await SendGroupAsync(floorGroup);

                    if (diningSessionId.HasValue)
                    {
                        // Publish control message so all API instances in the cluster evict local connections
                        var publisher = new RedisRealtimeEventPublisher(_redisProvider, LoggerFactory.Create(b => {}).CreateLogger<RedisRealtimeEventPublisher>());
                        await publisher.PublishSessionInvalidatedAsync(tenantId, branchId, diningSessionId.Value);
                        await EvictConnectionsForSessionAsync(tenantId, branchId, diningSessionId.Value);
                    }
                    break;

                case RealtimeEventTypes.MenuItemAvailabilityChanged:
                case RealtimeEventTypes.CatalogPolicyInvalidated:
                    await SendGroupAsync(catalogGroup);
                    await SendGroupAsync(floorGroup);
                    break;

                default:
                    await SendGroupAsync(floorGroup);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route Redis broadcast message to SignalR groups.");
        }
    }

    internal async Task HandleSessionEvictionMessageAsync(RedisValue message)
    {
        if (message.IsNullOrEmpty) return;

        try
        {
            using var doc = JsonDocument.Parse(message.ToString());
            var root = doc.RootElement;
            var tenantId = root.GetProperty("tenant_id").GetGuid();
            var branchId = root.GetProperty("branch_id").GetGuid();
            var sessionId = root.GetProperty("session_id").GetGuid();

            await EvictConnectionsForSessionAsync(tenantId, branchId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process session eviction control message.");
        }
    }

    internal async Task HandleAuthInvalidatedAsync(RedisValue message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message.ToString());
            Guid? sid = doc.RootElement.TryGetProperty("session_id", out var s) ? s.GetGuid() : null;
            Guid? uid = doc.RootElement.TryGetProperty("user_id", out var u) ? u.GetGuid() : null;
            foreach (var c in _tracker.GetAllConnections())
                if ((sid.HasValue && c.StaffSessionId == sid) || (uid.HasValue && c.UserId == uid)) await TerminateAsync(c);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Invalid authentication eviction message."); }
    }

    private async Task TerminateAsync(TrackedConnection c)
    {
        try
        {
            foreach (var group in c.SubscribedGroups ?? []) await _hubContext.Groups.RemoveFromGroupAsync(c.ConnectionId, group);
            await _hubContext.Clients.Client(c.ConnectionId).SendAsync("AccessRevoked", new { reason = "authorization_changed" });
        }
        finally { c.Abort?.Invoke(); _tracker.Untrack(c.ConnectionId); }
    }

    internal async Task HandleUserEvictionMessageAsync(RedisValue message)
    {
        if (message.IsNullOrEmpty) return;

        try
        {
            using var doc = JsonDocument.Parse(message.ToString());
            var root = doc.RootElement;
            var tenantId = root.GetProperty("tenant_id").GetGuid();
            var userId = root.GetProperty("user_id").GetGuid();

            Guid? sessionId = null;
            if (root.TryGetProperty("session_id", out var sidProp) && sidProp.ValueKind == JsonValueKind.String && sidProp.TryGetGuid(out var parsedSid))
            {
                sessionId = parsedSid;
            }

            var reason = "revoked";
            if (root.TryGetProperty("reason", out var reasonProp))
            {
                reason = reasonProp.GetString() ?? reason;
            }

            await EvictConnectionsForUserAsync(tenantId, userId, sessionId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process user eviction control message.");
        }
    }

    internal async Task EvictConnectionsForSessionAsync(Guid tenantId, Guid branchId, Guid sessionId)
    {
        var activeConnections = _tracker.GetConnectionsForSession(tenantId, branchId, sessionId);
        foreach (var conn in activeConnections)
        {
            try
            {
                // Send terminal notification
                await _hubContext.Clients.Client(conn.ConnectionId).SendAsync("SessionClosed", new { session_id = sessionId });

                // Server-side group lifecycle termination: remove connection from all SignalR groups
                if (conn.SubscribedGroups != null)
                {
                    foreach (var group in conn.SubscribedGroups)
                    {
                        await _hubContext.Groups.RemoveFromGroupAsync(conn.ConnectionId, group);
                    }
                }

                // Forcefully abort the transport connection on the server
                conn.Abort?.Invoke();

                // Untrack from local registry
                _tracker.Untrack(conn.ConnectionId);

                _logger.LogInformation("Evicted and terminated connection '{ConnectionId}' following session '{SessionId}' closure.",
                    conn.ConnectionId, sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanly evict connection '{ConnectionId}'.", conn.ConnectionId);
            }
            finally { conn.Abort?.Invoke(); _tracker.Untrack(conn.ConnectionId); }
        }
    }

    internal async Task EvictConnectionsForUserAsync(Guid tenantId, Guid userId, Guid? sessionId, string reason)
    {
        var activeConnections = _tracker.GetConnectionsForUser(tenantId, userId);
        foreach (var conn in activeConnections)
        {
            if (sessionId.HasValue && conn.StaffSessionId.HasValue && conn.StaffSessionId.Value != sessionId.Value)
            {
                continue;
            }

            try
            {
                await _hubContext.Clients.Client(conn.ConnectionId).SendAsync("AccessRevoked", new { user_id = userId, reason });

                if (conn.SubscribedGroups != null)
                {
                    foreach (var group in conn.SubscribedGroups)
                    {
                        await _hubContext.Groups.RemoveFromGroupAsync(conn.ConnectionId, group);
                    }
                }

                conn.Abort?.Invoke();
                _tracker.Untrack(conn.ConnectionId);

                _logger.LogInformation("Evicted and terminated staff connection '{ConnectionId}' for user '{UserId}' (reason: {Reason}).",
                    conn.ConnectionId, userId, reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanly evict staff connection '{ConnectionId}'.", conn.ConnectionId);
            }
            finally { conn.Abort?.Invoke(); _tracker.Untrack(conn.ConnectionId); }
        }
    }
}
