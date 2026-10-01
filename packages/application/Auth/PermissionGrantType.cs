namespace RestaurantOrder.Application.Auth;

/// <summary>
/// RBAC matrix authorization grant classification for a given role and permission.
/// </summary>
public enum PermissionGrantType
{
    /// <summary>Strictly prohibited (marked as ✗ in RBAC matrix). Access denied.</summary>
    Denied = 0,

    /// <summary>Full access permitted within the principal's boundary scope (marked as ✓ in RBAC matrix).</summary>
    Full = 1,

    /// <summary>
    /// Access permitted only within own or assigned resource scope (marked as O in RBAC matrix).
    /// Requires evaluation against IResourceOwnershipRequirement.
    /// </summary>
    OwnOrAssigned = 2
}
