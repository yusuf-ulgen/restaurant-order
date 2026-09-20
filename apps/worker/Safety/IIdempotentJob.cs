namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Contract requiring background jobs, queue messages, and printer tasks
/// to carry an immutable idempotency key to prevent duplicate execution across worker replicas.
/// </summary>
public interface IIdempotentJob
{
    /// <summary>
    /// Unique business identifier (e.g., "print:order-1234:receipt" or "payment-webhook:evt_9988").
    /// </summary>
    string IdempotencyKey { get; }

    /// <summary>
    /// Classification of the background job.
    /// </summary>
    string JobType { get; }
}
