using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Events;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

public class OperationalOutboxDispatcherTests
{
    private class TestEventPublisher : IRealtimeEventPublisher
    {
        public int CallCount { get; private set; }
        public string? LastPayload { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task PublishBroadcastAsync(string eventPayloadJson, CancellationToken ct = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated Redis failure.");
            }

            CallCount++;
            LastPayload = eventPayloadJson;
            return Task.CompletedTask;
        }

        public Task PublishSessionInvalidatedAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task PublishUserInvalidatedAsync(Guid tenantId, Guid userId, Guid? sessionId = null, string reason = "revoked", CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private static RestaurantOrderDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(databaseName: $"outbox_test_{Guid.NewGuid():N}")
            .Options;

        return new RestaurantOrderDbContext(options);
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_WhenNoMessages_ReturnsZero()
    {
        using var db = CreateInMemoryDbContext();
        var publisher = new TestEventPublisher();
        var dispatcher = new OperationalOutboxDispatcher(db, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingBatchAsync(10);

        Assert.Equal(0, dispatched);
        Assert.Equal(0, publisher.CallCount);
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_DispatchesPendingEvents_AndMarksDispatched()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = new TenantId(Guid.NewGuid());
        var branchId = new BranchId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;

        var message = OperationalOutboxMessage.Create(
            tenantId,
            branchId,
            "agg-123",
            "order.created",
            "{\"event_id\":\"test-1\"}",
            "idem-1",
            now,
            now);

        db.OperationalOutbox.Add(message);
        await db.SaveChangesAsync();

        var publisher = new TestEventPublisher();
        var dispatcher = new OperationalOutboxDispatcher(db, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingBatchAsync(10);

        Assert.Equal(1, dispatched);
        Assert.Equal(1, publisher.CallCount);
        Assert.Equal("{\"event_id\":\"test-1\"}", publisher.LastPayload);

        var updated = await db.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == message.Id);
        Assert.NotNull(updated);
        Assert.Equal(OperationalOutboxStatus.Dispatched, updated.Status);
        Assert.NotNull(updated.DispatchedAtUtc);
        Assert.Null(updated.LockClaimId);
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_OnTransientFailure_IncrementsAttemptsAndRequeues()
    {
        using var db = CreateInMemoryDbContext();
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()),
            new BranchId(Guid.NewGuid()),
            "agg-456",
            "order.created",
            "{\"event_id\":\"test-2\"}",
            "idem-2",
            now,
            now);

        db.OperationalOutbox.Add(message);
        await db.SaveChangesAsync();

        var publisher = new TestEventPublisher { ShouldThrow = true };
        var dispatcher = new OperationalOutboxDispatcher(db, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingBatchAsync(10);

        Assert.Equal(0, dispatched);
        var updated = await db.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == message.Id);
        Assert.NotNull(updated);
        Assert.Equal(OperationalOutboxStatus.Pending, updated.Status);
        Assert.Equal(1, updated.AttemptCount);
        Assert.NotNull(updated.LastError);
        Assert.Contains("Simulated Redis failure", updated.LastError);
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_OnMaxAttemptsExceeded_TransitionsToDeadLetter()
    {
        using var db = CreateInMemoryDbContext();
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()),
            new BranchId(Guid.NewGuid()),
            "agg-789",
            "order.created",
            "{\"event_id\":\"poison\"}",
            "idem-poison",
            now,
            now,
            maxAttempts: 1);

        db.OperationalOutbox.Add(message);
        await db.SaveChangesAsync();

        var publisher = new TestEventPublisher { ShouldThrow = true };
        var dispatcher = new OperationalOutboxDispatcher(db, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingBatchAsync(10);

        Assert.Equal(0, dispatched);
        var updated = await db.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == message.Id);
        Assert.NotNull(updated);
        Assert.Equal(OperationalOutboxStatus.DeadLetter, updated.Status);
        Assert.Equal(1, updated.AttemptCount);
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_StaleProcessingLease_ReclaimsAndDispatches()
    {
        using var db = CreateInMemoryDbContext();
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()),
            new BranchId(Guid.NewGuid()),
            "agg-stale",
            "table.session_closed",
            "{\"event_id\":\"stale-recovered\"}",
            "idem-stale",
            now.AddMinutes(-5),
            now.AddMinutes(-5));

        message.Claim("crashed-worker", now.AddMinutes(-4));
        db.OperationalOutbox.Add(message);
        await db.SaveChangesAsync();

        var publisher = new TestEventPublisher();
        var dispatcher = new OperationalOutboxDispatcher(db, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingBatchAsync(10);

        Assert.Equal(1, dispatched);
        var updated = await db.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == message.Id);
        Assert.NotNull(updated);
        Assert.Equal(OperationalOutboxStatus.Dispatched, updated.Status);
        Assert.Equal("{\"event_id\":\"stale-recovered\"}", publisher.LastPayload);
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_WhenLeaseInvalid_AbortsBatchPublication()
    {
        using var db = CreateInMemoryDbContext();
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()),
            new BranchId(Guid.NewGuid()),
            "agg-loss",
            "order.created",
            "{\"event_id\":\"loss-1\"}",
            "idem-loss",
            now,
            now);

        db.OperationalOutbox.Add(message);
        await db.SaveChangesAsync();

        var publisher = new TestEventPublisher();
        var dispatcher = new OperationalOutboxDispatcher(db, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingBatchAsync(10, isLeaseValid: _ => Task.FromResult(false));

        Assert.Equal(0, dispatched);
        Assert.Equal(0, publisher.CallCount);
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_WhenPayloadIsPoison_DeadLettersDirectlyWithoutPublishing()
    {
        using var db = CreateInMemoryDbContext();
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()),
            new BranchId(Guid.NewGuid()),
            "agg-poison",
            "order.created",
            "{{{ invalid json structure !!!",
            "idem-poison-json",
            now,
            now);

        db.OperationalOutbox.Add(message);
        await db.SaveChangesAsync();

        var publisher = new TestEventPublisher();
        var dispatcher = new OperationalOutboxDispatcher(db, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingBatchAsync(10);

        Assert.Equal(0, dispatched);
        Assert.Equal(0, publisher.CallCount);

        var updated = await db.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == message.Id);
        Assert.NotNull(updated);
        Assert.Equal(OperationalOutboxStatus.DeadLetter, updated.Status);
        Assert.NotNull(updated.LastError);
        Assert.Contains("Poison", updated.LastError);
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_OnCancellation_DoesNotIncrementFailureOrPersistError()
    {
        using var db = CreateInMemoryDbContext();
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()),
            new BranchId(Guid.NewGuid()),
            "agg-cancel",
            "order.created",
            "{\"event_id\":\"cancel-1\"}",
            "idem-cancel",
            now,
            now);

        db.OperationalOutbox.Add(message);
        await db.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var publisher = new TestEventPublisher();
        var dispatcher = new OperationalOutboxDispatcher(db, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchPendingBatchAsync(10, ct: cts.Token);

        Assert.Equal(0, dispatched);
        Assert.Equal(0, publisher.CallCount);

        var updated = await db.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == message.Id);
        Assert.NotNull(updated);
        Assert.Equal(0, updated.AttemptCount);
        Assert.Null(updated.LastError);
    }

    [Fact]
    public void SanitizeErrorMessage_MasksSensitiveInformation()
    {
        var rawError = "Connection failed: Server=127.0.0.1;Password=SuperSecretPass!;User ID=admin;token=ey1234567890 secret";
        var sanitized = OperationalOutboxDispatcher.SanitizeErrorMessage(rawError);

        Assert.DoesNotContain("SuperSecretPass!", sanitized);
        Assert.Contains("Password=***", sanitized);
        Assert.Contains("token=***", sanitized);
    }
}
