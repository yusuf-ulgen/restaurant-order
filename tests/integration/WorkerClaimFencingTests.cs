using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Infrastructure.Notifications;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Worker.Safety;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class WorkerClaimFencingTests(TestcontainersFixture fixture) : IClassFixture<TestcontainersFixture>
{
    [Fact]
    public async Task LeaseLoss_DuringSlowProvider_OldWorkerCannotComplete_NewWorkerRecovers()
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        var scope = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(scope);
        await using var admin = h.Admin();
        var now = DateTimeOffset.UtcNow;
        var device = DeviceRegistration.CreateForStaff(scope.Tenant, scope.Branch, staff.User, "synthetic_fence", "web", now, TimeSpan.FromDays(1));
        var delivery = NotificationDelivery.Create(scope.Tenant, scope.Branch, Guid.NewGuid(), "order.created", device.Id,
            device.InstallationId, "Staff", staff.User.Value, now, TimeSpan.FromMinutes(5));
        admin.AddRange(device, delivery); await admin.SaveChangesAsync();
        using var redis = await ConnectionMultiplexer.ConnectAsync(fixture.RedisEndpoint);
        using var firstLease = new RedisWorkerLeaseManager(redis, NullLogger<RedisWorkerLeaseManager>.Instance, "owner-A");
        using var secondLease = new RedisWorkerLeaseManager(redis, NullLogger<RedisWorkerLeaseManager>.Instance, "owner-B");
        var key = $"synthetic:lease:{Guid.NewGuid():N}";
        Assert.True(await firstLease.TryAcquireLeaseAsync(key, TimeSpan.FromSeconds(10)));
        using var guard = new RedisWorkerActivationGuard(redis, NullLogger<RedisWorkerActivationGuard>.Instance);
        await redis.GetDatabase().StringSetAsync(RedisWorkerActivationGuard.DefaultActiveSlotKey, "blue");
        await using var leadership = new LeadershipLeaseScope(guard, firstLease, key, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(10), CancellationToken.None);
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>().UseNpgsql(await fixture.ProvisionTemporaryWorkerRoleAsync()).Options;
        await using var dbA = new RestaurantOrderDbContext(options);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new Mock<IPushNotificationTransport>();
        slow.Setup(t => t.SendAsync(It.IsAny<PushNotificationRequest>(), It.IsAny<CancellationToken>())).Returns(async () => {
            entered.TrySetResult(); await finish.Task; return new PushSendResult(PushSendStatus.ProviderAccepted);
        });
        var first = new PushNotificationDispatcher(dbA, slow.Object, Mock.Of<ICustomerSessionValidator>(), NullLogger<PushNotificationDispatcher>.Instance);
        var running = first.DispatchPendingBatchAsync(isLeaseValid: leadership.ValidateAsync, tenantId: scope.Tenant.Value, ct: leadership.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Expire the Redis key deterministically, then transfer leadership to another worker.
        await redis.GetDatabase().KeyExpireAsync(key, TimeSpan.Zero);
        Assert.True(await secondLease.TryAcquireLeaseAsync(key, TimeSpan.FromSeconds(10)));
        Assert.False(await leadership.ValidateAsync(CancellationToken.None));
        finish.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        var unchanged = await admin.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.Id == delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.Claimed, unchanged.Status);
        await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE tenancy.notification_deliveries SET lock_expires_utc = clock_timestamp() - interval '1 second' WHERE id = {delivery.Id}");
        await using var dbB = new RestaurantOrderDbContext(options);
        var fast = new Mock<IPushNotificationTransport>();
        fast.Setup(t => t.SendAsync(It.IsAny<PushNotificationRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PushSendResult(PushSendStatus.ProviderAccepted));
        var second = new PushNotificationDispatcher(dbB, fast.Object, Mock.Of<ICustomerSessionValidator>(), NullLogger<PushNotificationDispatcher>.Instance);
        Assert.Equal(1, await second.DispatchPendingBatchAsync(tenantId: scope.Tenant.Value));
        var completed = await admin.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.Id == delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.ProviderAccepted, completed.Status);
        Assert.Equal(2, completed.AttemptCount);
    }
}
