using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Central registry governing capability permissions and RBAC matrix evaluations.
/// Adheres strictly to the deny-by-default security invariant.
/// </summary>
public interface IPermissionRegistry
{
    /// <summary>
    /// Returns the RBAC matrix grant classification for a specified role and permission capability.
    /// Returns PermissionGrantType.Denied if unmapped or unknown.
    /// </summary>
    PermissionGrantType GetGrantType(AuthRole role, string permission);

    /// <summary>
    /// Checks whether the specified role has an unconditional Full grant for the given permission.
    /// Note: Does not evaluate OwnOrAssigned grants (which require a resource context).
    /// </summary>
    bool HasFullGrant(AuthRole role, string permission);

    /// <summary>
    /// Evaluates whether an authenticated principal is authorized for the requested permission capability,
    /// taking into account boundary scopes, grant types, and fine-grained resource ownership.
    /// </summary>
    bool IsPermitted(
        AuthenticatedPrincipal principal,
        string permission,
        ResourceOwnershipContext? resourceContext = null);

    /// <summary>
    /// Returns all permissions granted (either Full or OwnOrAssigned) for the specified role.
    /// </summary>
    IReadOnlyCollection<string> GetPermissionsForRole(AuthRole role);
}
