using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Events;

public enum OperationalOutboxStatus
{
    Pending = 1,
    Processing = 2,
    Dispatched = 3,
    DeadLetter = 4
}

/// <summary>
/// Domain entity representing a transactional outbox message for operational domain events.
/// Persisted atomically within the same database transaction as domain mutations.
/// </summary>
public sealed class OperationalOutboxMessage
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public string AggregateId { get; private set; }
    public string EventType { get; private set; }
    public string Payload { get; private set; }
    public int SchemaVersion { get; private set; }
    public long SequenceNumber { get; private set; }
    public OperationalOutboxStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset? NextAttemptUtc { get; private set; }
    public string? LastError { get; private set; }
    public string? LockClaimId { get; private set; }
    public DateTimeOffset? LockExpiresUtc { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }

    private OperationalOutboxMessage()
    {
        AggregateId = string.Empty;
        EventType = string.Empty;
        Payload = string.Empty;
        IdempotencyKey = string.Empty;
    }

    public static OperationalOutboxMessage Create(
        TenantId tenantId,
        BranchId branchId,
        string aggregateId,
        string eventType,
        string payload,
        string idempotencyKey,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset createdAtUtc,
        int schemaVersion = 1,
        int maxAttempts = 5,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(aggregateId))
            throw new DomainException("AggregateId cannot be empty.");

        if (string.IsNullOrWhiteSpace(eventType))
            throw new DomainException("EventType cannot be empty.");

        if (string.IsNullOrWhiteSpace(payload))
            throw new DomainException("Payload cannot be empty.");

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("IdempotencyKey cannot be empty.");

        return new OperationalOutboxMessage
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            AggregateId = aggregateId.Trim(),
            EventType = eventType.Trim(),
            Payload = payload,
            SchemaVersion = schemaVersion > 0 ? schemaVersion : 1,
            Status = OperationalOutboxStatus.Pending,
            IdempotencyKey = idempotencyKey.Trim(),
            AttemptCount = 0,
            MaxAttempts = maxAttempts > 0 ? maxAttempts : 5,
            NextAttemptUtc = createdAtUtc,
            LastError = null,
            LockClaimId = null,
            LockExpiresUtc = null,
            OccurredAtUtc = occurredAtUtc,
            CreatedAtUtc = createdAtUtc,
            DispatchedAtUtc = null
        };
    }

    public void Claim(string claimId, DateTimeOffset lockExpiresUtc)
    {
        if (string.IsNullOrWhiteSpace(claimId))
            throw new DomainException("ClaimId cannot be empty.");

        Status = OperationalOutboxStatus.Processing;
        LockClaimId = claimId.Trim();
        LockExpiresUtc = lockExpiresUtc;
    }

    public void ReleaseClaim()
    {
        Status = OperationalOutboxStatus.Pending;
        LockClaimId = null;
        LockExpiresUtc = null;
    }

    public void MarkDispatched(DateTimeOffset dispatchedAtUtc)
    {
        Status = OperationalOutboxStatus.Dispatched;
        DispatchedAtUtc = dispatchedAtUtc;
        LockClaimId = null;
        LockExpiresUtc = null;
    }

    public void MarkFailed(string error, DateTimeOffset? nextAttemptUtc, bool isDeadLetter)
    {
        Status = isDeadLetter ? OperationalOutboxStatus.DeadLetter : OperationalOutboxStatus.Pending;
        LastError = error != null && error.Length > 500 ? error[..500] : error;
        NextAttemptUtc = nextAttemptUtc;
        AttemptCount++;
        LockClaimId = null;
        LockExpiresUtc = null;
    }
}
