namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Distributed idempotency store contract.
/// Guarantees at-most-once execution for queue processing, thermal receipt printing,
/// and external webhooks across concurrent Blue-Green worker instances.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to reserve an idempotency key atomically.
    /// Returns true if this is the first execution (reservation succeeded).
    /// Returns false if the key is already processing or completed (duplicate message).
    /// </summary>
    Task<bool> TryReserveKeyAsync(
        string idempotencyKey,
        TimeSpan retention,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the job identified by the idempotency key as successfully completed.
    /// </summary>
    Task MarkCompletedAsync(
        string idempotencyKey,
        TimeSpan retention,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the job identified by the idempotency key has already been completed.
    /// </summary>
    Task<bool> IsCompletedAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
