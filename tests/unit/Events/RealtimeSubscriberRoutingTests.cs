using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Api.Realtime;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

public class RealtimeSubscriberRoutingTests
{
    [Theory]
    [InlineData("order.created", true)]
    [InlineData("order.created", false)]
    [InlineData("order.item_status_changed", true)]
    [InlineData("order.item_status_changed", false)]
    [InlineData("table.service_requested", true)]
    [InlineData("table.service_requested", false)]
    [InlineData("table.service_request_status_changed", true)]
    [InlineData("table.service_request_status_changed", false)]
    [InlineData("table.session_closed", true)]
    [InlineData("table.session_closed", false)]
    [InlineData("menu.item_availability_changed", true)]
    [InlineData("catalog.policy_invalidated", false)]
    public async Task TypedRouting_SendsOncePerAuthorizedLocalConnection_AndNeverToForeignScopes(string type, bool hasSession)
    {
        var tenant = Guid.NewGuid(); var branch = Guid.NewGuid(); var session = Guid.NewGuid();
        var prefix = $"tenant:{tenant}:branch:{branch}:";
        var tracker = new RealtimeConnectionTracker();
        var customerAborted = false;
        tracker.Track(new("customer", tenant, branch, "Customer", session, null,
            SubscribedGroups: [prefix + $"session:{session}", prefix + "catalog"], Abort: () => customerAborted = true));
        tracker.Track(new("floor", tenant, branch, "Waiter", null, Guid.NewGuid(), SubscribedGroups: [prefix + "floor", prefix + "catalog"]));
        tracker.Track(new("kitchen", tenant, branch, "Kitchen", null, Guid.NewGuid(), SubscribedGroups: [prefix + "station:kitchen"]));
        tracker.Track(new("bar", tenant, branch, "Bar", null, Guid.NewGuid(), SubscribedGroups: [prefix + "station:bar"]));
        tracker.Track(new("foreign", Guid.NewGuid(), Guid.NewGuid(), "Waiter", null, Guid.NewGuid(), SubscribedGroups: ["foreign:floor"]));
        var delivered = new List<string>();
        var clients = new Mock<IHubClients>();
        foreach (var id in new[] { "customer", "floor", "kitchen", "bar", "foreign" })
        {
            var proxy = new Mock<ISingleClientProxy>();
            proxy.Setup(p => p.SendCoreAsync("ReceiveEvent", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Callback(() => delivered.Add(id)).Returns(Task.CompletedTask);
            clients.Setup(c => c.Client(id)).Returns(proxy.Object);
        }
        var hub = new Mock<IHubContext<EventsHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        hub.Setup(h => h.Groups).Returns(Mock.Of<IGroupManager>());
        var subscriber = new Mock<ISubscriber>();
        subscriber.Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>())).ReturnsAsync(1);
        var redis = new Mock<IRedisDatabaseProvider>();
        redis.Setup(r => r.GetRequiredSubscriberAsync(It.IsAny<CancellationToken>())).ReturnsAsync(subscriber.Object);
        var authority = new Mock<IRealtimeConnectionAuthorizer>();
        authority.Setup(a => a.IsAuthorizedAsync(It.IsAny<TrackedConnection>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new RedisEventSubscriberService(redis.Object, hub.Object, tracker,
            NullLogger<RedisEventSubscriberService>.Instance, authority.Object);
        object data = hasSession ? new { dining_session_id = session } : new { };
        await service.HandleBroadcastMessageAsync(JsonSerializer.Serialize(new {
            event_id = Guid.NewGuid(), tenant_id = tenant, branch_id = branch, event_type = type, data }));
        var expected = new List<string> { "floor" };
        if (type.StartsWith("order.")) expected.AddRange(["kitchen", "bar"]);
        if (hasSession || type is "menu.item_availability_changed" or "catalog.policy_invalidated") expected.Add("customer");
        Assert.Equal(expected.Order(), delivered.Order());
        Assert.Equal(delivered.Count, delivered.Distinct().Count());
        Assert.Equal(type == "table.session_closed" && hasSession, customerAborted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"tenant_id\":null}")]
    public async Task MalformedBroadcast_CannotProducePrivateDelivery(string payload)
    {
        var hub = new Mock<IHubContext<EventsHub>>(MockBehavior.Strict);
        var service = new RedisEventSubscriberService(Mock.Of<IRedisDatabaseProvider>(), hub.Object,
            new RealtimeConnectionTracker(), NullLogger<RedisEventSubscriberService>.Instance, Mock.Of<IRealtimeConnectionAuthorizer>());
        await service.HandleBroadcastMessageAsync(payload);
        hub.VerifyNoOtherCalls();
    }
}
