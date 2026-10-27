using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Notifications;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Notifications;

public class PushStagingLifetimeTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public async Task DelayedStaging_UsesEventAge_InsteadOfRestartingTtl(int minutesOld)
    {
        await using var db = new RestaurantOrderDbContext(new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new TenantId(Guid.NewGuid());
        var branch = new BranchId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        db.DeviceRegistrations.Add(DeviceRegistration.CreateForStaff(tenant, branch, new UserId(Guid.NewGuid()),
            "synthetic_device", "web", now, TimeSpan.FromDays(1)));
        await db.SaveChangesAsync();
        var stager = new PushNotificationDeliveryStager(db, NullLogger<PushNotificationDeliveryStager>.Instance);
        var occurred = now.AddMinutes(-minutesOld);
        await stager.StageDeliveriesForEventAsync(tenant.Value, branch.Value, Guid.NewGuid(), "order.created", "order", "{}", occurred);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync();
        if (minutesOld >= 5) Assert.Empty(deliveries);
        else Assert.Equal(occurred.AddMinutes(5), Assert.Single(deliveries).ExpiresAtUtc);
    }
}
