using RestaurantOrder.Application.Tenancy;

namespace RestaurantOrder.Api.Tenancy;

/// <summary>
/// Default production-safe resolver. Returns empty context until Phase 3 authentication is implemented.
/// Ensures unverified client headers are never trusted in production or staging.
/// </summary>
public class DefaultTenantContextResolver : ITenantContextResolver
{
    public Task<ITenantContext> ResolveAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(TenantContext.Empty);
    }
}
