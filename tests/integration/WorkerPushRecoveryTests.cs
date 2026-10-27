using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Infrastructure.Events;
using RestaurantOrder.Infrastructure.Notifications;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class WorkerPushRecoveryTests(TestcontainersFixture fixture) : IClassFixture<TestcontainersFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StagingFailureOrCrash_RollsBackDeliveries_ThenReplaysExactlyOnePersistentTask(bool crash)
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        var branch = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(branch);
        await using var admin = h.Admin();
        var now = DateTimeOffset.UtcNow;
        var device = DeviceRegistration.CreateForStaff(branch.Tenant, branch.Branch, staff.User, "synthetic_recovery", "web", now, TimeSpan.FromDays(1));
        var message = OperationalOutboxMessage.Create(branch.Tenant, branch.Branch, "order", "order.created", "{}", Guid.NewGuid().ToString(), now, now);
        admin.AddRange(device, message);
        await admin.SaveChangesAsync();
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>().UseNpgsql(await fixture.ProvisionTemporaryWorkerRoleAsync()).Options;
        await using var first = new RestaurantOrderDbContext(options);
        var realStager = new PushNotificationDeliveryStager(first, NullLogger<PushNotificationDeliveryStager>.Instance);
        using var stopped = new CancellationTokenSource();
        var brokenStager = new Mock<IPushNotificationDeliveryStager>();
        brokenStager.Setup(s => s.StageDeliveriesForEventAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid t, Guid b, Guid id, string type, string aggregate, string payload, DateTimeOffset occurred, CancellationToken ct) => {
                await realStager.StageDeliveriesForEventAsync(t, b, id, type, aggregate, payload, occurred, ct);
                if (crash)
                {
                    // Süreç kaybının PostgreSQL sınırı: yazılmış fakat commit edilmemiş
                    // worker bağlantısını sunucudan kopar; yalnızca cooperative rollback'e dayanma.
                    var backend = await first.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(ct);
                    await admin.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_terminate_backend({backend})");
                    stopped.Cancel();
                    throw new OperationCanceledException(ct);
                }
                throw new InvalidOperationException("Synthetic failure after staging write");
            });
        var publisher = new Mock<IRealtimeEventPublisher>();
        var dispatcher = new OperationalOutboxDispatcher(first, publisher.Object, NullLogger<OperationalOutboxDispatcher>.Instance, brokenStager.Object);
        Assert.Equal(0, await dispatcher.DispatchPendingBatchAsync(tenantId: branch.Tenant.Value, ct: stopped.Token));
        Assert.False(first.HasTenant);
        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(await admin.NotificationDeliveries.IgnoreQueryFilters().Where(d => d.EventId == message.Id).ToListAsync());
        var failed = await admin.OperationalOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == message.Id);
        Assert.Equal(crash ? OperationalOutboxStatus.Processing : OperationalOutboxStatus.Pending, failed.Status);
        await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE tenancy.operational_outbox SET next_attempt_utc = clock_timestamp() - interval '1 second', lock_expires_utc = clock_timestamp() - interval '1 second' WHERE id = {message.Id}");
        await using var second = new RestaurantOrderDbContext(options);
        var recoveredStager = new PushNotificationDeliveryStager(second, NullLogger<PushNotificationDeliveryStager>.Instance);
        var recovered = new OperationalOutboxDispatcher(second, publisher.Object, NullLogger<OperationalOutboxDispatcher>.Instance, recoveredStager);
        Assert.Equal(1, await recovered.DispatchPendingBatchAsync(tenantId: branch.Tenant.Value));
        Assert.Equal(0, await recovered.DispatchPendingBatchAsync(tenantId: branch.Tenant.Value));
        var task = Assert.Single(await admin.NotificationDeliveries.IgnoreQueryFilters().Where(d => d.EventId == message.Id).AsNoTracking().ToListAsync());
        Assert.Equal(message.Id, task.EventId);
        Assert.Equal(failed.OccurredAtUtc.AddMinutes(5), task.ExpiresAtUtc);
        // PostgreSQL uniqueness also protects concurrent standalone staging/replay.
        await using var third = new RestaurantOrderDbContext(options);
        var concurrent = new PushNotificationDeliveryStager(third, NullLogger<PushNotificationDeliveryStager>.Instance);
        await Task.WhenAll(recoveredStager.StageDeliveriesForEventAsync(branch.Tenant.Value, branch.Branch.Value, message.Id,
            "order.created", "order", "{}", now), concurrent.StageDeliveriesForEventAsync(branch.Tenant.Value, branch.Branch.Value,
            message.Id, "order.created", "order", "{}", now));
        Assert.Single(await admin.NotificationDeliveries.IgnoreQueryFilters().Where(d => d.EventId == message.Id).AsNoTracking().ToListAsync());
    }
}
