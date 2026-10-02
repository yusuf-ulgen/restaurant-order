using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Tenancy;

namespace RestaurantOrder.Api.Tenancy;

/// <summary>
/// Production-safe resolver extracting authenticated tenant and branch context strictly from
/// verified ClaimsPrincipal claims. Ensures unverified client headers are never trusted.
/// </summary>
public class DefaultTenantContextResolver : ITenantContextResolver
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public DefaultTenantContextResolver(IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<ITenantContext> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null || httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(TenantContext.Empty);
        }

        var tenantIdClaim = httpContext.User.FindFirst(JwtClaimNames.TenantId)?.Value;
        if (Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            Guid? branchId = null;
            var branchIdClaim = httpContext.User.FindFirst(JwtClaimNames.BranchId)?.Value;
            if (Guid.TryParse(branchIdClaim, out var bId))
            {
                branchId = bId;
            }

            return Task.FromResult<ITenantContext>(new TenantContext(tenantId, branchId, isAuthenticated: true));
        }

        return Task.FromResult(TenantContext.Empty);
    }
}
