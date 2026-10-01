namespace RestaurantOrder.Application.Tenancy;

/// <summary>
/// Executes a background job within an isolated tenant context.
/// Enforces fail-closed behavior for missing/empty tenant IDs and guarantees
/// context cleanup in a finally block so that context does not leak between sequential or parallel jobs.
/// </summary>
public interface ITenantWorkerJobRunner
{
    Task ExecuteAsync(
        ITenantJobEnvelope envelope,
        Func<ITenantContext, CancellationToken, Task> jobAction,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteAsync<TResult>(
        ITenantJobEnvelope envelope,
        Func<ITenantContext, CancellationToken, Task<TResult>> jobAction,
        CancellationToken cancellationToken = default);
}
