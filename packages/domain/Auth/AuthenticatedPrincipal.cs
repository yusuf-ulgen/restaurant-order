using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Immutable domain model representing an authenticated principal (staff or customer).
/// Enforces all RBAC, tenancy boundary, and authentication method invariants.
/// </summary>
public sealed record AuthenticatedPrincipal
{
    public Guid SubjectId { get; }
    public PrincipalType PrincipalType { get; }
    public AuthRole Role { get; }
    public AuthorizationScope Scope { get; }
    public Guid SessionId { get; }
    public AuthenticationMethod AuthMethod { get; }
    public int SecurityVersion { get; }

    public TenantId? TenantId => Scope.TenantId;
    public BranchId? BranchId => Scope.BranchId;
    public Guid? TableSessionId => Scope.TableSessionId;

    public bool IsSuperAdmin => Role == AuthRole.SuperAdmin;
    public bool IsStaff => PrincipalType == PrincipalType.Staff;
    public bool IsCustomer => PrincipalType == PrincipalType.Customer;

    public AuthenticatedPrincipal(
        Guid subjectId,
        PrincipalType principalType,
        AuthRole role,
        AuthorizationScope scope,
        Guid sessionId,
        AuthenticationMethod authMethod,
        int securityVersion)
    {
        if (subjectId == Guid.Empty)
        {
            throw new InvalidAuthorizationScopeException("SubjectId cannot be an empty Guid.");
        }

        if (sessionId == Guid.Empty)
        {
            throw new InvalidAuthorizationScopeException("SessionId cannot be an empty Guid.");
        }

        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        SubjectId = subjectId;
        PrincipalType = principalType;
        Role = role;
        SessionId = sessionId;
        AuthMethod = authMethod;
        SecurityVersion = securityVersion;

        // Enforce scope and role compatibility
        Scope.ValidateRoleCompatibility(Role);

        // Enforce principal-type invariants
        if (PrincipalType == PrincipalType.Staff)
        {
            if (Role.IsCustomerRole())
            {
                throw new InvalidAuthorizationScopeException("Staff principal cannot be assigned the Customer role.");
            }

            if (AuthMethod == AuthenticationMethod.CustomerQrSession)
            {
                throw new InvalidAuthorizationScopeException(
                    "Staff principal cannot use CustomerQrSession authentication method.");
            }

            if (SecurityVersion < 1)
            {
                throw new InvalidAuthorizationScopeException(
                    "Staff principal security version must be greater than or equal to 1.");
            }
        }
        else if (PrincipalType == PrincipalType.Customer)
        {
            if (Role != AuthRole.Customer)
            {
                throw new InvalidAuthorizationScopeException(
                    $"Customer principal cannot be assigned role '{Role}'. Role must be Customer.");
            }

            if (AuthMethod != AuthenticationMethod.CustomerQrSession)
            {
                throw new InvalidAuthorizationScopeException(
                    "Customer principal must authenticate via CustomerQrSession.");
            }

            if (SecurityVersion != 0)
            {
                throw new InvalidAuthorizationScopeException(
                    "Customer principal security version must be 0.");
            }
        }
        else
        {
            throw new InvalidAuthorizationScopeException($"Unknown PrincipalType '{PrincipalType}'.");
        }

        // Enforce PIN bounds (strictly branch staff terminals)
        if (AuthMethod == AuthenticationMethod.Pin && !Role.IsBranchRole())
        {
            throw new InvalidAuthorizationScopeException(
                $"PIN authentication is strictly permitted for branch staff roles. Role '{Role}' cannot authenticate via PIN.");
        }
    }

    /// <summary>Creates an authenticated staff principal.</summary>
    public static AuthenticatedPrincipal CreateStaff(
        Guid userId,
        AuthRole role,
        AuthorizationScope scope,
        Guid sessionId,
        AuthenticationMethod authMethod,
        int securityVersion) =>
        new(userId, PrincipalType.Staff, role, scope, sessionId, authMethod, securityVersion);

    /// <summary>Creates an authenticated customer principal bound to a table session.</summary>
    public static AuthenticatedPrincipal CreateCustomer(
        Guid tableSessionId,
        TenantId tenantId,
        BranchId branchId) =>
        new(
            subjectId: tableSessionId,
            principalType: PrincipalType.Customer,
            role: AuthRole.Customer,
            scope: AuthorizationScope.ForTableSession(tenantId, branchId, tableSessionId),
            sessionId: tableSessionId,
            authMethod: AuthenticationMethod.CustomerQrSession,
            securityVersion: 0);
}
