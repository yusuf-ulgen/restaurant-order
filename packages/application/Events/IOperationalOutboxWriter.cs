namespace RestaurantOrder.Application.Events;

/// <summary>
/// Service to enqueue operational domain events atomically within the active database transaction.
/// Ensures zero phantom events on transaction rollback and strict tenant isolation.
/// </summary>
public interface IOperationalOutboxWriter
{
    void EnqueueEvent<T>(
        Guid tenantId,
        Guid branchId,
        string aggregateId,
        string eventType,
        T data,
        string idempotencyKey,
        DateTimeOffset? occurredAtUtc = null,
        int schemaVersion = 1,
        Guid? eventId = null);
}
