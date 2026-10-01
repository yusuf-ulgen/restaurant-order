namespace RestaurantOrder.Application.Tenancy;

/// <summary>
/// Thread-safe, async-local implementation of <see cref="ITenantContextAccessor"/>.
/// Guarantees that tenant context flows across asynchronous calls and can be cleaned up reliably.
/// </summary>
public class AsyncLocalTenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<ITenantContext?> CurrentContext = new();

    public ITenantContext TenantContext
    {
        get => CurrentContext.Value ?? RestaurantOrder.Application.Tenancy.TenantContext.Empty;
        set => CurrentContext.Value = value ?? RestaurantOrder.Application.Tenancy.TenantContext.Empty;
    }
}
