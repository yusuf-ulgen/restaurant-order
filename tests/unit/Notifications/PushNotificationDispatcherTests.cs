using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Notifications;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Notifications;

public class PushNotificationDispatcherTests
{
    private static RestaurantOrderDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(databaseName: $"push_disp_test_{Guid.NewGuid():N}")
            .Options;

        return new RestaurantOrderDbContext(options);
    }

    [Fact]
    public async Task DispatchPendingBatch_WhenProviderAccepted_MarksDeliveryCompleted()
    {
        using var db = CreateInMemoryDbContext();
        var transportMock = new Mock<IPushNotificationTransport>();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var device = DeviceRegistration.CreateForCustomer(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            "inst_accepted_123",
            "web",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(1));
        db.DeviceRegistrations.Add(device);

        var delivery = NotificationDelivery.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            Guid.NewGuid(),
            "order.item_status_changed",
            device.Id,
            device.InstallationId,
            "Customer",
            sessionId,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        transportMock
            .Setup(t => t.SendAsync(It.IsAny<PushNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult(PushSendStatus.ProviderAccepted));

        var dispatcher = new PushNotificationDispatcher(
            db,
            transportMock.Object,
            sessionValidatorMock.Object,
            NullLogger<PushNotificationDispatcher>.Instance);

        var count = await dispatcher.DispatchPendingBatchAsync(10);

        Assert.Equal(1, count);
        var updated = await db.NotificationDeliveries.IgnoreQueryFilters().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.ProviderAccepted, updated.Status);
        Assert.Null(updated.LockClaimId);
        Assert.NotNull(updated.ProcessedAtUtc);
    }

    [Fact]
    public async Task DispatchPendingBatch_WhenRetryableFailureWithRetryAfter_SchedulesRetry()
    {
        using var db = CreateInMemoryDbContext();
        var transportMock = new Mock<IPushNotificationTransport>();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var initialEventId = Guid.NewGuid();

        var device = DeviceRegistration.CreateForCustomer(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            "inst_retry_456",
            "web",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(1));
        db.DeviceRegistrations.Add(device);

        var delivery = NotificationDelivery.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            initialEventId,
            "order.created",
            device.Id,
            device.InstallationId,
            "Customer",
            sessionId,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var retryAfter = TimeSpan.FromSeconds(45);
        transportMock
            .Setup(t => t.SendAsync(It.IsAny<PushNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult(PushSendStatus.RetryableFailure, retryAfter));

        var dispatcher = new PushNotificationDispatcher(
            db,
            transportMock.Object,
            sessionValidatorMock.Object,
            NullLogger<PushNotificationDispatcher>.Instance);

        var count = await dispatcher.DispatchPendingBatchAsync(10);

        Assert.Equal(0, count);
        var updated = await db.NotificationDeliveries.IgnoreQueryFilters().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.FailedRetryable, updated.Status);
        Assert.Equal(1, updated.AttemptCount);
        Assert.NotNull(updated.NextAttemptUtc);
        Assert.Equal(initialEventId, updated.EventId); // Stable EventId
    }

    [Fact]
    public async Task DispatchPendingBatch_WhenInvalidRecipient_DeactivatesDeviceAndFailsDelivery()
    {
        using var db = CreateInMemoryDbContext();
        var transportMock = new Mock<IPushNotificationTransport>();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var device = DeviceRegistration.CreateForCustomer(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            "inst_invalid_token",
            "android",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(1));
        db.DeviceRegistrations.Add(device);

        var delivery = NotificationDelivery.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            Guid.NewGuid(),
            "order.created",
            device.Id,
            device.InstallationId,
            "Customer",
            sessionId,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        transportMock
            .Setup(t => t.SendAsync(It.IsAny<PushNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult(PushSendStatus.InvalidRecipient));

        var dispatcher = new PushNotificationDispatcher(
            db,
            transportMock.Object,
            sessionValidatorMock.Object,
            NullLogger<PushNotificationDispatcher>.Instance);

        await dispatcher.DispatchPendingBatchAsync(10);

        var updatedDelivery = await db.NotificationDeliveries.IgnoreQueryFilters().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.FailedPermanent, updatedDelivery.Status);

        var updatedDevice = await db.DeviceRegistrations.IgnoreQueryFilters().FirstAsync(d => d.Id == device.Id);
        Assert.False(updatedDevice.IsActive); // Device safely deactivated!
    }

    [Fact]
    public async Task DispatchPendingBatch_WhenEventExpired_DoesNotSendAndMarksExpired()
    {
        using var db = CreateInMemoryDbContext();
        var transportMock = new Mock<IPushNotificationTransport>();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var device = DeviceRegistration.CreateForCustomer(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            "inst_expired_event",
            "web",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(1));
        db.DeviceRegistrations.Add(device);

        // Event occurred 10 minutes ago with 5-minute TTL -> expired!
        var delivery = NotificationDelivery.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            Guid.NewGuid(),
            "order.item_status_changed",
            device.Id,
            device.InstallationId,
            "Customer",
            sessionId,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            TimeSpan.FromMinutes(5));
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var dispatcher = new PushNotificationDispatcher(
            db,
            transportMock.Object,
            sessionValidatorMock.Object,
            NullLogger<PushNotificationDispatcher>.Instance);

        await dispatcher.DispatchPendingBatchAsync(10);

        // Transport SendAsync must NEVER be called for expired events
        transportMock.Verify(t => t.SendAsync(It.IsAny<PushNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        var updated = await db.NotificationDeliveries.IgnoreQueryFilters().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.Expired, updated.Status);
    }

    [Fact]
    public async Task DispatchPendingBatch_WhenCustomerSessionClosedBeforeSend_FailsDeliveryAndDeactivatesDevice()
    {
        using var db = CreateInMemoryDbContext();
        var transportMock = new Mock<IPushNotificationTransport>();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var device = DeviceRegistration.CreateForCustomer(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            "inst_closed_session",
            "web",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(1));
        db.DeviceRegistrations.Add(device);

        var delivery = NotificationDelivery.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            Guid.NewGuid(),
            "order.item_status_changed",
            device.Id,
            device.InstallationId,
            "Customer",
            sessionId,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        // Customer checked out or session was closed!
        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var dispatcher = new PushNotificationDispatcher(
            db,
            transportMock.Object,
            sessionValidatorMock.Object,
            NullLogger<PushNotificationDispatcher>.Instance);

        await dispatcher.DispatchPendingBatchAsync(10);

        // Transport SendAsync must not be called
        transportMock.Verify(t => t.SendAsync(It.IsAny<PushNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        var updatedDelivery = await db.NotificationDeliveries.IgnoreQueryFilters().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.FailedPermanent, updatedDelivery.Status);

        var updatedDevice = await db.DeviceRegistrations.IgnoreQueryFilters().FirstAsync(d => d.Id == device.Id);
        Assert.False(updatedDevice.IsActive);
    }

    [Fact]
    public async Task DispatchPendingBatch_StuckClaimedDeliveryAfterCrash_IsRecovered()
    {
        using var db = CreateInMemoryDbContext();
        var transportMock = new Mock<IPushNotificationTransport>();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var device = DeviceRegistration.CreateForCustomer(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            "inst_crash_recover",
            "web",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(1));
        db.DeviceRegistrations.Add(device);

        var delivery = NotificationDelivery.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            Guid.NewGuid(),
            "order.created",
            device.Id,
            device.InstallationId,
            "Customer",
            sessionId,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));

        // Simulate crash: delivery was claimed, but previous worker died and lock expired!
        delivery.Claim("crashed-worker-claim", DateTimeOffset.UtcNow.AddSeconds(-10));
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        transportMock
            .Setup(t => t.SendAsync(It.IsAny<PushNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult(PushSendStatus.ProviderAccepted));

        var dispatcher = new PushNotificationDispatcher(
            db,
            transportMock.Object,
            sessionValidatorMock.Object,
            NullLogger<PushNotificationDispatcher>.Instance);

        var count = await dispatcher.DispatchPendingBatchAsync(10);

        Assert.Equal(1, count);
        var updated = await db.NotificationDeliveries.IgnoreQueryFilters().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.ProviderAccepted, updated.Status);
    }
}
