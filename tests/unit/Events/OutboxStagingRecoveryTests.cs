using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Events;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

public class OutboxStagingRecoveryTests
{
    [Fact]
    public async Task StagingFailure_KeepsEventRetryable_InsteadOfCompletingIt()
    {
        await using var db = new RestaurantOrderDbContext(new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(new TenantId(Guid.NewGuid()), new BranchId(Guid.NewGuid()),
            "order", "order.created", "{}", "staging-failure", now, now);
        db.OperationalOutbox.Add(message);
        await db.SaveChangesAsync();
        var staging = new Mock<IPushNotificationDeliveryStager>();
        staging.Setup(s => s.StageDeliveriesForEventAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Staging unavailable"));
        var publisher = new Mock<IRealtimeEventPublisher>();
        var dispatcher = new OperationalOutboxDispatcher(db, publisher.Object,
            NullLogger<OperationalOutboxDispatcher>.Instance, staging.Object);

        Assert.Equal(0, await dispatcher.DispatchPendingBatchAsync());
        var persisted = await db.OperationalOutbox.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(OperationalOutboxStatus.Pending, persisted.Status);
        Assert.NotNull(persisted.NextAttemptUtc);
    }
}
