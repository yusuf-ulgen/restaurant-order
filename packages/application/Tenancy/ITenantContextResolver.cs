namespace RestaurantOrder.Application.Tenancy;

/// <summary>
/// Strategy for resolving the tenant context from the current execution environment
/// (e.g. HTTP request, job envelope, or test context).
/// </summary>
public interface ITenantContextResolver
{
    Task<ITenantContext> ResolveAsync(CancellationToken cancellationToken = default);
}
