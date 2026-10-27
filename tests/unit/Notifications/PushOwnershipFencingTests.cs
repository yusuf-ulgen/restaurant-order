using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Notifications;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Notifications;

public class PushOwnershipFencingTests
{
    [Fact]
    public async Task ReboundDevice_DoesNotReceivePreviousOwnersDelivery()
    {
        await using var db = new RestaurantOrderDbContext(new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTimeOffset.UtcNow;
        var tenant = new TenantId(Guid.NewGuid()); var branch = new BranchId(Guid.NewGuid());
        var oldSession = new DiningSessionId(Guid.NewGuid());
        var device = DeviceRegistration.CreateForCustomer(tenant, branch, oldSession, "synthetic_device", "web", now, TimeSpan.FromDays(1));
        var delivery = NotificationDelivery.Create(tenant, branch, Guid.NewGuid(), "order.item_status_changed", device.Id,
            device.InstallationId, "Customer", oldSession.Value, now, TimeSpan.FromMinutes(5));
        device.RebindToCustomer(branch, new DiningSessionId(Guid.NewGuid()), "web", now, TimeSpan.FromDays(1));
        db.AddRange(device, delivery);
        await db.SaveChangesAsync();
        var transport = new Mock<IPushNotificationTransport>();
        var validator = new Mock<ICustomerSessionValidator>();
        validator.Setup(v => v.ValidateSessionActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var dispatcher = new PushNotificationDispatcher(db, transport.Object, validator.Object, NullLogger<PushNotificationDispatcher>.Instance);
        Assert.Equal(0, await dispatcher.DispatchPendingBatchAsync());
        transport.Verify(t => t.SendAsync(It.IsAny<PushNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(NotificationDeliveryStatus.FailedPermanent,
            (await db.NotificationDeliveries.IgnoreQueryFilters().SingleAsync()).Status);
        Assert.True((await db.DeviceRegistrations.IgnoreQueryFilters().SingleAsync()).IsActive);
    }
}
