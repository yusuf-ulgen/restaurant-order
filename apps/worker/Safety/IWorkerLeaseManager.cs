namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Architectural Contract: Distributed Worker Lease Manager.
/// [Requires Distributed Lease Provider: Redis Redlock or PostgreSQL Advisory Lock]
///
/// In dynamic blue-green deployments where active slot cutover occurs without
/// restarting worker containers, this interface coordinates active leadership lease
/// across blue and green worker replicas.
///
/// Invariants:
/// 1. Only one replica across both slots holds the lease for a given queue or domain.
/// 2. If the lease cannot be renewed before expiry, the worker MUST immediately fail-closed.
/// 3. All background jobs must enforce idempotency keys even with lease protection.
/// </summary>
public interface IWorkerLeaseManager
{
    /// <summary>
    /// Attempts to acquire an exclusive distributed lease for the specified key and duration.
    /// </summary>
    Task<bool> TryAcquireLeaseAsync(string leaseKey, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews an existing lease before it expires.
    /// </summary>
    Task<bool> RenewLeaseAsync(string leaseKey, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly releases the lease upon planned shutdown or cutover drain.
    /// </summary>
    Task ReleaseLeaseAsync(string leaseKey, CancellationToken cancellationToken = default);
}
