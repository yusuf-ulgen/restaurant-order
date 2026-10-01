namespace RestaurantOrder.Application.Tenancy;

/// <summary>
/// Immutable implementation of <see cref="ITenantContext"/>.
/// Provides fail-closed evaluation: empty or null GUIDs are treated as absent context.
/// </summary>
public class TenantContext : ITenantContext
{
    public static readonly ITenantContext Empty = new TenantContext();

    public Guid? TenantId { get; }
    public Guid? BranchId { get; }
    public bool HasTenant => TenantId.HasValue && TenantId.Value != Guid.Empty;
    public bool IsAuthenticated { get; }

    public TenantContext(Guid? tenantId = null, Guid? branchId = null, bool isAuthenticated = false)
    {
        TenantId = tenantId == Guid.Empty ? null : tenantId;
        BranchId = branchId == Guid.Empty ? null : branchId;
        IsAuthenticated = isAuthenticated && HasTenant;
    }

    public Guid RequireTenantId()
    {
        if (!HasTenant || !IsAuthenticated)
        {
            throw new TenantContextException(
                "Tenant context is required but missing, unauthenticated, or invalid. Operation blocked (fail-closed).");
        }

        return TenantId!.Value;
    }
}
