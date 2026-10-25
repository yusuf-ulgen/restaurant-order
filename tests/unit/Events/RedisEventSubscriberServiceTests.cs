using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Api.Realtime;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

public class RedisEventSubscriberServiceTests
{
    private readonly Mock<IRedisDatabaseProvider> _redisProviderMock = new();
    private readonly Mock<IDatabase> _redisDbMock = new();
    private readonly Mock<IHubContext<EventsHub>> _hubContextMock = new();
    private readonly Mock<IHubClients> _clientsMock = new();
    private readonly Mock<ISingleClientProxy> _clientProxyMock = new();
    private readonly Mock<IGroupManager> _groupsMock = new();
    private readonly RealtimeConnectionTracker _tracker = new();

    public RedisEventSubscriberServiceTests()
    {
        _redisProviderMock.Setup(p => p.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_redisDbMock.Object);
        _redisDbMock.SetReturnsDefault(Task.FromResult(true));

        _hubContextMock.Setup(h => h.Clients).Returns(_clientsMock.Object);
        _hubContextMock.Setup(h => h.Groups).Returns(_groupsMock.Object);
        _clientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(_clientProxyMock.Object);
        _clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxyMock.Object);
    }

    private RedisEventSubscriberService CreateService()
    {
        return new RedisEventSubscriberService(
            _redisProviderMock.Object,
            _hubContextMock.Object,
            _tracker,
            NullLogger<RedisEventSubscriberService>.Instance);
    }

    [Fact]
    public async Task SessionEviction_AbortsTransportAndRemovesAllSubscribedGroups()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var connId = "cust-conn-1";

        var abortCalled = false;
        var groups = new List<string>
        {
            $"tenant:{tenantId}:branch:{branchId}:session:{sessionId}",
            $"tenant:{tenantId}:branch:{branchId}:catalog"
        };

        _tracker.Track(new TrackedConnection(
            ConnectionId: connId,
            TenantId: tenantId,
            BranchId: branchId,
            Role: "Customer",
            DiningSessionId: sessionId,
            UserId: null,
            SubscribedGroups: groups,
            Abort: () => abortCalled = true
        ));

        var service = CreateService();

        var evictionPayload = JsonSerializer.Serialize(new
        {
            tenant_id = tenantId,
            branch_id = branchId,
            session_id = sessionId
        });

        await service.HandleSessionEvictionMessageAsync(evictionPayload);

        // Verify terminal SessionClosed message sent
        _clientProxyMock.Verify(c => c.SendCoreAsync("SessionClosed", It.IsAny<object[]>(), default), Times.Once);

        // Verify server-side group removal
        foreach (var grp in groups)
        {
            _groupsMock.Verify(g => g.RemoveFromGroupAsync(connId, grp, default), Times.Once);
        }

        // Verify transport abort
        Assert.True(abortCalled, "Expected connection.Abort() to be called on session eviction.");

        // Verify untracked
        Assert.Empty(_tracker.GetConnectionsForSession(tenantId, branchId, sessionId));
    }

    [Fact]
    public async Task UserEviction_AbortsTransportAndRemovesAllSubscribedGroups()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var connId = "staff-conn-1";

        var abortCalled = false;
        var groups = new List<string>
        {
            $"tenant:{tenantId}:branch:{branchId}:floor",
            $"tenant:{tenantId}:branch:{branchId}:catalog"
        };

        _tracker.Track(new TrackedConnection(
            ConnectionId: connId,
            TenantId: tenantId,
            BranchId: branchId,
            Role: "Waiter",
            DiningSessionId: null,
            UserId: userId,
            SubscribedGroups: groups,
            Abort: () => abortCalled = true
        ));

        var service = CreateService();

        var evictionPayload = JsonSerializer.Serialize(new
        {
            tenant_id = tenantId,
            user_id = userId,
            reason = "token_revoked"
        });

        await service.HandleUserEvictionMessageAsync(evictionPayload);

        // Verify terminal AccessRevoked message sent
        _clientProxyMock.Verify(c => c.SendCoreAsync("AccessRevoked", It.IsAny<object[]>(), default), Times.Once);

        // Verify server-side group removal
        foreach (var grp in groups)
        {
            _groupsMock.Verify(g => g.RemoveFromGroupAsync(connId, grp, default), Times.Once);
        }

        // Verify transport abort
        Assert.True(abortCalled, "Expected connection.Abort() to be called on staff user revocation.");

        // Verify untracked
        Assert.Empty(_tracker.GetConnectionsForUser(tenantId, userId));
    }

    [Fact]
    public async Task OrderItemStatusChanged_RoutesToCustomerSessionFloorAndStationQueues()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var service = CreateService();

        var envelope = new
        {
            event_id = eventId,
            event_type = RealtimeEventTypes.OrderItemStatusChanged,
            schema_version = 1,
            occurred_at_utc = DateTimeOffset.UtcNow,
            tenant_id = tenantId,
            branch_id = branchId,
            aggregate_id = Guid.NewGuid().ToString(),
            sequence_number = 100L,
            data = new
            {
                order_id = Guid.NewGuid(),
                order_item_id = Guid.NewGuid(),
                dining_session_id = sessionId,
                previous_status = "InPreparation",
                new_status = "Ready",
                station = "Kitchen"
            }
        };

        var json = JsonSerializer.Serialize(envelope);

        await service.HandleBroadcastMessageAsync(json);

        var sessionGroup = $"tenant:{tenantId}:branch:{branchId}:session:{sessionId}";
        var floorGroup = $"tenant:{tenantId}:branch:{branchId}:floor";
        var kitchenGroup = $"tenant:{tenantId}:branch:{branchId}:station:kitchen";
        var barGroup = $"tenant:{tenantId}:branch:{branchId}:station:bar";

        _clientsMock.Verify(c => c.Group(sessionGroup), Times.Once);
        _clientsMock.Verify(c => c.Group(floorGroup), Times.Once);
        _clientsMock.Verify(c => c.Group(kitchenGroup), Times.Once);
        _clientsMock.Verify(c => c.Group(barGroup), Times.Once);
    }

    [Fact]
    public async Task TokenExpirySweep_AbortsExpiredConnectionAndRemovesGroups()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var connId = "expired-staff-conn";

        var abortCalled = false;
        var groups = new List<string>
        {
            $"tenant:{tenantId}:branch:{branchId}:floor",
            $"tenant:{tenantId}:branch:{branchId}:catalog"
        };

        var expiredTime = DateTimeOffset.UtcNow.AddMinutes(-5);

        _tracker.Track(new TrackedConnection(
            ConnectionId: connId,
            TenantId: tenantId,
            BranchId: branchId,
            Role: "Waiter",
            DiningSessionId: null,
            UserId: userId,
            TokenExpiresAtUtc: expiredTime,
            SubscribedGroups: groups,
            Abort: () => abortCalled = true
        ));

        var service = CreateService();

        await service.SweepExpiredConnectionsAsync(DateTimeOffset.UtcNow);

        // Verify TokenExpired message sent
        _clientProxyMock.Verify(c => c.SendCoreAsync("TokenExpired", It.IsAny<object[]>(), default), Times.Once);

        // Verify server-side group removal
        foreach (var grp in groups)
        {
            _groupsMock.Verify(g => g.RemoveFromGroupAsync(connId, grp, default), Times.Once);
        }

        // Verify transport abort
        Assert.True(abortCalled, "Expected connection.Abort() to be called on token expiration.");

        // Verify untracked
        Assert.Empty(_tracker.GetConnectionsForUser(tenantId, userId));
    }
}
