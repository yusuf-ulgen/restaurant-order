namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Architectural boundary scope governing authorization and data isolation.
/// </summary>
public enum AuthorizationScopeType
{
    /// <summary>Platform-wide root scope (SuperAdmin only). No tenant or branch binding.</summary>
    Platform = 1,

    /// <summary>Tenant-level scope (RestaurantAdmin). Bound to a single Tenant ID.</summary>
    Tenant = 2,

    /// <summary>Branch-level scope (BranchManager, Cashier, Kitchen, Bar, Waiter). Bound to Tenant and Branch IDs.</summary>
    Branch = 3,

    /// <summary>Table-session scope (Customer). Bound to Tenant, Branch, and Table Session IDs.</summary>
    TableSession = 4
}
