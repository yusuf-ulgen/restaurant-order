using System;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

public class OperationalOutboxEntityTests
{
    [Fact]
    public void Create_InitializesMessageInPendingStateWithZeroAttempts()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var branchId = new BranchId(Guid.NewGuid());
        var aggregateId = Guid.NewGuid().ToString();
        var eventType = "order.created";
        var payload = "{\"event_id\":\"test\"}";
        var idempotencyKey = $"order-{aggregateId}";
        var occurredAt = DateTimeOffset.UtcNow;
        var createdAt = occurredAt;

        var message = OperationalOutboxMessage.Create(
            tenantId,
            branchId,
            aggregateId,
            eventType,
            payload,
            idempotencyKey,
            occurredAt,
            createdAt);

        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal(tenantId, message.TenantId);
        Assert.Equal(branchId, message.BranchId);
        Assert.Equal(aggregateId, message.AggregateId);
        Assert.Equal(eventType, message.EventType);
        Assert.Equal(payload, message.Payload);
        Assert.Equal(idempotencyKey, message.IdempotencyKey);
        Assert.Equal(occurredAt, message.OccurredAtUtc);
        Assert.Equal(createdAt, message.CreatedAtUtc);
        Assert.Equal(OperationalOutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.AttemptCount);
        Assert.Equal(5, message.MaxAttempts);
        Assert.Equal(0, message.SequenceNumber);
        Assert.Null(message.LockClaimId);
        Assert.Null(message.LockExpiresUtc);
        Assert.Null(message.DispatchedAtUtc);
        Assert.Null(message.LastError);
    }

    [Fact]
    public void Claim_TransitionsToProcessingStateWithLockDetails()
    {
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()), new BranchId(Guid.NewGuid()), "agg-1", "order.created", "{}", "idem-1", now, now);

        var expires = now.AddSeconds(30);
        message.Claim("worker-node-1", expires);

        Assert.Equal(OperationalOutboxStatus.Processing, message.Status);
        Assert.Equal("worker-node-1", message.LockClaimId);
        Assert.Equal(expires, message.LockExpiresUtc);
    }

    [Fact]
    public void MarkDispatched_TransitionsToDispatchedAndClearsLock()
    {
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()), new BranchId(Guid.NewGuid()), "agg-1", "order.created", "{}", "idem-1", now, now);
        message.Claim("worker-node-1", now.AddSeconds(30));

        var dispatchedAt = now.AddSeconds(2);
        message.MarkDispatched(dispatchedAt);

        Assert.Equal(OperationalOutboxStatus.Dispatched, message.Status);
        Assert.Equal(dispatchedAt, message.DispatchedAtUtc);
        Assert.Null(message.LockClaimId);
        Assert.Null(message.LockExpiresUtc);
    }

    [Fact]
    public void MarkFailed_IncrementsAttemptsAndRequeues_WhenUnderMaxAttempts()
    {
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()), new BranchId(Guid.NewGuid()), "agg-1", "order.created", "{}", "idem-1", now, now);
        message.Claim("worker-node-1", now.AddSeconds(30));

        var nextAttempt = now.AddSeconds(5);
        message.MarkFailed("Redis connection dropped", nextAttempt, isDeadLetter: false);

        Assert.Equal(OperationalOutboxStatus.Pending, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("Redis connection dropped", message.LastError);
        Assert.Equal(nextAttempt, message.NextAttemptUtc);
        Assert.Null(message.LockClaimId);
        Assert.Null(message.LockExpiresUtc);
    }

    [Fact]
    public void MarkFailed_TransitionsToDeadLetter_WhenDeadLetterFlagTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()), new BranchId(Guid.NewGuid()), "agg-1", "order.created", "{}", "idem-1", now, now);

        message.Claim("worker-node-1", now.AddSeconds(30));
        message.MarkFailed("Poison message unhandled exception", null, isDeadLetter: true);

        Assert.Equal(OperationalOutboxStatus.DeadLetter, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("Poison message unhandled exception", message.LastError);
        Assert.Null(message.LockClaimId);
        Assert.Null(message.LockExpiresUtc);
    }

    [Fact]
    public void ReleaseClaim_ReturnsToPendingState()
    {
        var now = DateTimeOffset.UtcNow;
        var message = OperationalOutboxMessage.Create(
            new TenantId(Guid.NewGuid()), new BranchId(Guid.NewGuid()), "agg-1", "order.created", "{}", "idem-1", now, now);
        message.Claim("worker-node-1", now.AddSeconds(30));

        message.ReleaseClaim();

        Assert.Equal(OperationalOutboxStatus.Pending, message.Status);
        Assert.Null(message.LockClaimId);
        Assert.Null(message.LockExpiresUtc);
    }
}
