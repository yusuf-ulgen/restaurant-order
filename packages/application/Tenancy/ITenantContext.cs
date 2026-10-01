namespace RestaurantOrder.Application.Tenancy;

/// <summary>
/// Framework-independent tenant context abstraction.
/// Captures resolved Tenant ID, optional Branch ID, and authentication state.
/// Enforces fail-closed access at the application boundary.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? BranchId { get; }
    bool HasTenant { get; }
    bool IsAuthenticated { get; }

    /// <summary>
    /// Returns the active Tenant ID.
    /// Throws <see cref="TenantContextException"/> if context is missing, invalid, or unauthenticated.
    /// Guarantees fail-closed behavior.
    /// </summary>
    Guid RequireTenantId();
}
