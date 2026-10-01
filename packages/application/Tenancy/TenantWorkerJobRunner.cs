namespace RestaurantOrder.Application.Tenancy;

/// <summary>
/// Executes worker jobs within an isolated tenant context.
/// Enforces fail-closed validation: envelopes with empty Tenant IDs are rejected immediately.
/// Cleans up ambient context in a finally block to prevent leakage across sequential or pooled worker executions.
/// </summary>
public class TenantWorkerJobRunner : ITenantWorkerJobRunner
{
    private readonly ITenantContextAccessor _contextAccessor;

    public TenantWorkerJobRunner(ITenantContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    public async Task ExecuteAsync(
        ITenantJobEnvelope envelope,
        Func<ITenantContext, CancellationToken, Task> jobAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(jobAction);

        if (envelope.TenantId == Guid.Empty)
        {
            throw new TenantContextException("Background job envelope must contain a valid, non-empty Tenant ID. Fail-closed.");
        }

        var context = new TenantContext(envelope.TenantId, envelope.BranchId, isAuthenticated: true);
        var previousContext = _contextAccessor.TenantContext;

        try
        {
            _contextAccessor.TenantContext = context;
            await jobAction(context, cancellationToken);
        }
        finally
        {
            _contextAccessor.TenantContext = previousContext;
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        ITenantJobEnvelope envelope,
        Func<ITenantContext, CancellationToken, Task<TResult>> jobAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(jobAction);

        if (envelope.TenantId == Guid.Empty)
        {
            throw new TenantContextException("Background job envelope must contain a valid, non-empty Tenant ID. Fail-closed.");
        }

        var context = new TenantContext(envelope.TenantId, envelope.BranchId, isAuthenticated: true);
        var previousContext = _contextAccessor.TenantContext;

        try
        {
            _contextAccessor.TenantContext = context;
            return await jobAction(context, cancellationToken);
        }
        finally
        {
            _contextAccessor.TenantContext = previousContext;
        }
    }
}
