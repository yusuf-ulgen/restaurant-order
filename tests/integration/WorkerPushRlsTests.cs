using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Events;
using RestaurantOrder.Infrastructure.Notifications;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class WorkerPushRlsTests(TestcontainersFixture fixture) : IClassFixture<TestcontainersFixture>
{
    private RestaurantOrderDbContext Db(string connection) => new(new DbContextOptionsBuilder<RestaurantOrderDbContext>().UseNpgsql(connection).Options);

    [Fact]
    public async Task TwoTenants_StageAndDispatch_WithUnprivilegedWorker_WithoutContextLeakage()
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var admin = Db(fixture.DatabaseConnectionString);
        var tenants = new List<Guid>();
        foreach (var label in new[] { "A", "B" })
        {
            var now = DateTimeOffset.UtcNow;
            var tenant = Tenant.Create($"Synthetic {label}", $"t-{Guid.NewGuid():N}"[..16]);
            var brand = Brand.Create(tenant.Id, "Synthetic Brand", $"b-{Guid.NewGuid():N}"[..16]);
            var branch = Branch.Create(tenant.Id, brand.Id, "Synthetic Branch", $"br-{Guid.NewGuid():N}"[..16]);
            var user = User.Create($"synthetic-{Guid.NewGuid():N}@example.invalid", "synthetic-hash", now);
            var membership = UserMembership.Create(tenant.Id, user.Id, AuthRole.Waiter, branch.Id, now);
            admin.AddRange(tenant, brand, branch, user, membership);
            admin.DeviceRegistrations.Add(DeviceRegistration.CreateForStaff(tenant.Id, branch.Id, user.Id,
                $"synthetic_{Guid.NewGuid():N}", "web", now, TimeSpan.FromDays(1)));
            admin.OperationalOutbox.Add(OperationalOutboxMessage.Create(tenant.Id, branch.Id, "order", "order.created", "{}",
                $"event-{Guid.NewGuid():N}", now, now));
            tenants.Add(tenant.Id.Value);
        }
        await admin.SaveChangesAsync();
        await using var db = Db(await fixture.ProvisionTemporaryWorkerRoleAsync());
        var attributes = await db.Database.SqlQueryRaw<bool>("SELECT NOT rolsuper AND NOT rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync();
        Assert.True(attributes);
        var stager = new PushNotificationDeliveryStager(db, NullLogger<PushNotificationDeliveryStager>.Instance);
        var outbox = new OperationalOutboxDispatcher(db, Mock.Of<IRealtimeEventPublisher>(),
            NullLogger<OperationalOutboxDispatcher>.Instance, stager);
        Assert.Equal(2, await outbox.DispatchPendingBatchAsync());
        Assert.False(db.HasTenant);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0, await db.NotificationDeliveries.IgnoreQueryFilters().CountAsync());

        var seen = new List<Guid>();
        var transport = new Mock<IPushNotificationTransport>();
        transport.Setup(t => t.SendAsync(It.IsAny<PushNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (PushNotificationRequest request, CancellationToken ct) =>
            {
                seen.Add(await db.Database.SqlQueryRaw<Guid>("SELECT tenancy.get_current_tenant_id() AS \"Value\"").SingleAsync(ct));
                return new PushSendResult(PushSendStatus.ProviderAccepted);
            });
        var dispatcher = new PushNotificationDispatcher(db, transport.Object, Mock.Of<ICustomerSessionValidator>(),
            NullLogger<PushNotificationDispatcher>.Instance);
        Assert.Equal(2, await dispatcher.DispatchPendingBatchAsync());
        Assert.Equal(tenants.Order(), seen.Order());
        Assert.False(db.HasTenant);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0, await db.NotificationDeliveries.IgnoreQueryFilters().CountAsync());
        var deliveries = await admin.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, d => Assert.Equal(NotificationDeliveryStatus.ProviderAccepted, d.Status));
    }
}
