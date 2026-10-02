using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Immutable value object defining the boundary scope governing authorization and data isolation.
/// Validates scope invariants based on ScopeType.
/// </summary>
public sealed record AuthorizationScope
{
    public AuthorizationScopeType ScopeType { get; }
    public TenantId? TenantId { get; }
    public BranchId? BranchId { get; }
    public Guid? TableSessionId { get; }

    private AuthorizationScope(
        AuthorizationScopeType scopeType,
        TenantId? tenantId,
        BranchId? branchId,
        Guid? tableSessionId)
    {
        ScopeType = scopeType;
        TenantId = tenantId;
        BranchId = branchId;
        TableSessionId = tableSessionId;
    }

    /// <summary>Creates a platform-wide authorization scope (SuperAdmin only).</summary>
    public static AuthorizationScope Platform() =>
        new(AuthorizationScopeType.Platform, null, null, null);

    /// <summary>Creates a tenant-level authorization scope (RestaurantAdmin).</summary>
    public static AuthorizationScope ForTenant(TenantId tenantId) =>
        new(AuthorizationScopeType.Tenant, tenantId, null, null);

    /// <summary>Creates a branch-level authorization scope (Branch staff).</summary>
    public static AuthorizationScope ForBranch(TenantId tenantId, BranchId branchId) =>
        new(AuthorizationScopeType.Branch, tenantId, branchId, null);

    /// <summary>Creates a table-session authorization scope (Customer QR dining session).</summary>
    public static AuthorizationScope ForTableSession(TenantId tenantId, BranchId branchId, Guid tableSessionId)
    {
        if (tableSessionId == Guid.Empty)
        {
            throw new InvalidAuthorizationScopeException("TableSessionId cannot be an empty Guid.");
        }

        return new(AuthorizationScopeType.TableSession, tenantId, branchId, tableSessionId);
    }

    /// <summary>
    /// Validates whether the given role is architecturally compatible with this authorization scope.
    /// Throws InvalidAuthorizationScopeException if incompatible.
    /// </summary>
    public void ValidateRoleCompatibility(AuthRole role)
    {
        switch (ScopeType)
        {
            case AuthorizationScopeType.Platform:
                if (role != AuthRole.SuperAdmin)
                {
                    throw new InvalidAuthorizationScopeException(
                        $"Role '{role}' is not permitted in Platform scope. Only SuperAdmin may operate at Platform scope.");
                }
                break;

            case AuthorizationScopeType.Tenant:
                if (role != AuthRole.RestaurantAdmin)
                {
                    throw new InvalidAuthorizationScopeException(
                        $"Role '{role}' is not permitted in Tenant scope. Only RestaurantAdmin may operate at Tenant scope.");
                }
                break;

            case AuthorizationScopeType.Branch:
                if (!role.IsBranchRole())
                {
                    throw new InvalidAuthorizationScopeException(
                        $"Role '{role}' is not permitted in Branch scope. Only branch staff roles may operate at Branch scope.");
                }
                break;

            case AuthorizationScopeType.TableSession:
                if (role != AuthRole.Customer)
                {
                    throw new InvalidAuthorizationScopeException(
                        $"Role '{role}' is not permitted in TableSession scope. Only Customer may operate at TableSession scope.");
                }
                break;

            default:
                throw new InvalidAuthorizationScopeException($"Unknown authorization scope type '{ScopeType}'.");
        }
    }
}
