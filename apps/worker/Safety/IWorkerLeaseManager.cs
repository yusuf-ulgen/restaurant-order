namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Architectural Contract: Distributed Worker Lease Manager.
/// Uses Redis Redlock / atomic SET NX + TTL and Lua scripts to coordinate
/// active worker leadership across blue and green replicas.
///
/// Invariants:
/// 1. Only one replica across both slots holds the lease for a given queue or domain.
/// 2. If the lease cannot be renewed before expiry, the worker MUST immediately fail-closed.
/// 3. All background jobs must enforce idempotency keys even with lease protection.
/// 4. Renew and release are strictly restricted to the owner token.
/// </summary>
public interface IWorkerLeaseManager
{
    /// <summary>
    /// Cryptographically secure token uniquely identifying this lease manager instance.
    /// </summary>
    string OwnerToken { get; }

    /// <summary>
    /// Attempts to acquire an exclusive distributed lease for the specified key and duration.
    /// </summary>
    Task<bool> TryAcquireLeaseAsync(string leaseKey, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews an existing lease before it expires. Must verify owner token.
    /// </summary>
    Task<bool> RenewLeaseAsync(string leaseKey, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly releases the lease upon planned shutdown or cutover drain. Must verify owner token.
    /// </summary>
    Task ReleaseLeaseAsync(string leaseKey, CancellationToken cancellationToken = default);
}
