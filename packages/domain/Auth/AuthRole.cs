namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Supported authorization roles across all product surfaces in restaurant-order.
/// Matches the 8 distinct roles defined in docs/ROLES-AND-PERMISSIONS.md.
/// </summary>
public enum AuthRole
{
    /// <summary>Platform owner; system-wide administration, tenant onboarding, billing.</summary>
    SuperAdmin = 1,

    /// <summary>Brand/tenant owner; multi-branch administration, menus, financials.</summary>
    RestaurantAdmin = 2,

    /// <summary>Branch manager; daily operations, floor layouts, shifts, reports.</summary>
    BranchManager = 3,

    /// <summary>Cashier / register operator; bill collection, cash drawer, split payments.</summary>
    Cashier = 4,

    /// <summary>Kitchen food preparation station.</summary>
    Kitchen = 5,

    /// <summary>Bar drink preparation station.</summary>
    Bar = 6,

    /// <summary>Service waiter; tables, orders, service requests, bill view.</summary>
    Waiter = 7,

    /// <summary>Guest dining customer; QR menu, order placement, table bill view.</summary>
    Customer = 8
}

/// <summary>
/// Domain extension methods for role categorization and display metadata.
/// </summary>
public static class AuthRoleExtensions
{
    public static bool IsPlatformRole(this AuthRole role) =>
        role == AuthRole.SuperAdmin;

    public static bool IsTenantRole(this AuthRole role) =>
        role == AuthRole.RestaurantAdmin;

    public static bool IsBranchRole(this AuthRole role) =>
        role is AuthRole.BranchManager
            or AuthRole.Cashier
            or AuthRole.Kitchen
            or AuthRole.Bar
            or AuthRole.Waiter;

    public static bool IsStaffRole(this AuthRole role) =>
        role != AuthRole.Customer;

    public static bool IsCustomerRole(this AuthRole role) =>
        role == AuthRole.Customer;

    public static string GetTurkishDisplayName(this AuthRole role) => role switch
    {
        AuthRole.SuperAdmin => "Süper Admin",
        AuthRole.RestaurantAdmin => "Restoran Admini",
        AuthRole.BranchManager => "Şube Müdürü",
        AuthRole.Cashier => "Operasyon/Kasa",
        AuthRole.Kitchen => "Mutfak",
        AuthRole.Bar => "Bar",
        AuthRole.Waiter => "Garson",
        AuthRole.Customer => "Müşteri",
        _ => role.ToString()
    };
}
