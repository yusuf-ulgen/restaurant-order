using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Events;
using RestaurantOrder.Infrastructure.Notifications;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

public class WorkerClaimAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownLostLeadership_DoesNotClaimAnyNewQueueRow(bool push)
    {
        await using var db = new RestaurantOrderDbContext(new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new TenantId(Guid.NewGuid()); var branch = new BranchId(Guid.NewGuid()); var now = DateTimeOffset.UtcNow;
        var outbox = OperationalOutboxMessage.Create(tenant, branch, "order", "order.created", "{}", Guid.NewGuid().ToString(), now, now);
        var delivery = NotificationDelivery.Create(tenant, branch, Guid.NewGuid(), "order.created", Guid.NewGuid(),
            "synthetic_target", "Staff", Guid.NewGuid(), now, TimeSpan.FromMinutes(5));
        db.AddRange(outbox, delivery); await db.SaveChangesAsync();
        if (push)
        {
            var dispatcher = new PushNotificationDispatcher(db, Mock.Of<IPushNotificationTransport>(),
                Mock.Of<ICustomerSessionValidator>(), NullLogger<PushNotificationDispatcher>.Instance);
            Assert.Equal(0, await dispatcher.DispatchPendingBatchAsync(isLeaseValid: _ => Task.FromResult(false)));
        }
        else
        {
            var dispatcher = new OperationalOutboxDispatcher(db, Mock.Of<IRealtimeEventPublisher>(), NullLogger<OperationalOutboxDispatcher>.Instance);
            Assert.Equal(0, await dispatcher.DispatchPendingBatchAsync(isLeaseValid: _ => Task.FromResult(false)));
        }
        Assert.Equal(OperationalOutboxStatus.Pending, (await db.OperationalOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Status);
        Assert.Equal(NotificationDeliveryStatus.Pending, (await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Status);
    }
}
