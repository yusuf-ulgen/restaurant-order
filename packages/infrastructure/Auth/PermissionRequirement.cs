using Microsoft.AspNetCore.Authorization;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Authorization requirement demanding a specific machine-readable capability/permission.
/// Supports demanding a fresh session validation for high-risk operations.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public bool RequireFreshSession { get; }

    public PermissionRequirement(string permission, bool requireFreshSession = false)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            throw new ArgumentException("Permission capability cannot be empty.", nameof(permission));
        }

        Permission = permission.Trim();
        RequireFreshSession = requireFreshSession;
    }
}
