using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Catalog;

public enum CatalogOutboxStatus
{
    Pending = 1,
    Processing = 2,
    Dispatched = 3,
    DeadLetter = 4
}

/// <summary>
/// Domain entity representing a transactional outbox message for catalog availability changes.
/// Persisted atomically within the same database transaction as the availability state mutation,
/// guaranteeing zero phantom events upon transaction rollback or unhandled pipeline failures.
/// </summary>
public sealed class CatalogAvailabilityOutboxMessage
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public string AggregateId { get; private set; }
    public string EventType { get; private set; }
    public string Payload { get; private set; }
    public int SchemaVersion { get; private set; }
    public CatalogOutboxStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset? NextAttemptUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }

    private CatalogAvailabilityOutboxMessage()
    {
        AggregateId = string.Empty;
        EventType = string.Empty;
        Payload = string.Empty;
        IdempotencyKey = string.Empty;
    }

    public static CatalogAvailabilityOutboxMessage Create(
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
        {
            throw new DomainException("AggregateId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new DomainException("EventType cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new DomainException("Payload cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainException("IdempotencyKey cannot be empty.");
        }

        return new CatalogAvailabilityOutboxMessage
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            AggregateId = aggregateId.Trim(),
            EventType = eventType.Trim(),
            Payload = payload,
            SchemaVersion = schemaVersion > 0 ? schemaVersion : 1,
            Status = CatalogOutboxStatus.Pending,
            IdempotencyKey = idempotencyKey.Trim(),
            AttemptCount = 0,
            MaxAttempts = maxAttempts > 0 ? maxAttempts : 5,
            NextAttemptUtc = createdAtUtc,
            LastError = null,
            OccurredAtUtc = occurredAtUtc,
            CreatedAtUtc = createdAtUtc,
            DispatchedAtUtc = null
        };
    }

    public void MarkDispatched(DateTimeOffset dispatchedAtUtc)
    {
        Status = CatalogOutboxStatus.Dispatched;
        DispatchedAtUtc = dispatchedAtUtc;
    }

    public void MarkFailed(string error, DateTimeOffset? nextAttemptUtc, bool isDeadLetter)
    {
        Status = isDeadLetter ? CatalogOutboxStatus.DeadLetter : CatalogOutboxStatus.Pending;
        LastError = error != null && error.Length > 500 ? error[..500] : error;
        NextAttemptUtc = nextAttemptUtc;
        AttemptCount++;
    }
}
