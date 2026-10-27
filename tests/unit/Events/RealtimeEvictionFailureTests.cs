using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Api.Realtime;
using RestaurantOrder.Infrastructure.Redis;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

public class RealtimeEvictionFailureTests
{
    [Theory]
    [InlineData("session", false)]
    [InlineData("user", false)]
    [InlineData("expiry", false)]
    [InlineData("session", true)]
    [InlineData("user", true)]
    [InlineData("expiry", true)]
    public async Task Eviction_AlwaysAbortsAndUntracks_WhenTerminalSendOrGroupRemovalFails(string kind, bool groupFailure)
    {
        var tenant = Guid.NewGuid(); var branch = Guid.NewGuid(); var user = Guid.NewGuid(); var session = Guid.NewGuid();
        var tracker = new RealtimeConnectionTracker(); var aborted = 0;
        tracker.Track(new("target", tenant, branch, "Waiter", session, user, session,
            TokenExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1), SubscribedGroups: ["private"], Abort: () => aborted++));
        var proxy = new Mock<ISingleClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(groupFailure ? Task.CompletedTask : Task.FromException(new IOException("synthetic transport failure")));
        var clients = new Mock<IHubClients>(); clients.Setup(c => c.Client("target")).Returns(proxy.Object);
        var groups = new Mock<IGroupManager>();
        groups.Setup(g => g.RemoveFromGroupAsync("target", "private", It.IsAny<CancellationToken>()))
            .Returns(groupFailure ? Task.FromException(new IOException("synthetic group failure")) : Task.CompletedTask);
        var hub = new Mock<IHubContext<EventsHub>>(); hub.Setup(h => h.Clients).Returns(clients.Object); hub.Setup(h => h.Groups).Returns(groups.Object);
        var service = new RedisEventSubscriberService(Mock.Of<IRedisDatabaseProvider>(), hub.Object, tracker,
            NullLogger<RedisEventSubscriberService>.Instance, Mock.Of<IRealtimeConnectionAuthorizer>());
        if (kind == "session") await service.EvictConnectionsForSessionAsync(tenant, branch, session);
        else if (kind == "user") await service.EvictConnectionsForUserAsync(tenant, user, session, "revoked");
        else await service.SweepExpiredConnectionsAsync(DateTimeOffset.UtcNow);
        Assert.Equal(1, aborted);
        Assert.Empty(tracker.GetAllConnections());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthenticationInvalidation_TargetsSessionOrUserWithoutClosingUnrelatedIdentity(bool bySession)
    {
        var tracker = new RealtimeConnectionTracker(); var user = Guid.NewGuid(); var session = Guid.NewGuid();
        var aborted = false;
        tracker.Track(new("target", Guid.NewGuid(), Guid.NewGuid(), "Waiter", null, user, session, Abort: () => aborted = true));
        tracker.Track(new("other", Guid.NewGuid(), Guid.NewGuid(), "Waiter", null, Guid.NewGuid(), Guid.NewGuid()));
        var proxy = Mock.Of<ISingleClientProxy>();
        var clients = new Mock<IHubClients>(); clients.Setup(c => c.Client("target")).Returns(proxy);
        var hub = new Mock<IHubContext<EventsHub>>(); hub.Setup(h => h.Clients).Returns(clients.Object); hub.Setup(h => h.Groups).Returns(Mock.Of<IGroupManager>());
        var service = new RedisEventSubscriberService(Mock.Of<IRedisDatabaseProvider>(), hub.Object, tracker,
            NullLogger<RedisEventSubscriberService>.Instance, Mock.Of<IRealtimeConnectionAuthorizer>());
        await service.HandleAuthInvalidatedAsync(bySession ? JsonSerializer.Serialize(new { session_id = session }) : JsonSerializer.Serialize(new { user_id = user }));
        Assert.True(aborted);
        Assert.Equal("other", Assert.Single(tracker.GetAllConnections()).ConnectionId);
        await service.HandleAuthInvalidatedAsync("{}");
        await service.HandleAuthInvalidatedAsync("not-json");
        Assert.Single(tracker.GetAllConnections());
    }
}
