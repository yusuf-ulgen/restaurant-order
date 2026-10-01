using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class AuthRoleAndScopeTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly Guid _tableSessionId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();

    [Fact]
    public void AuthRole_HasExactlyEightDistinctRoles()
    {
        var roles = Enum.GetValues<AuthRole>();
        Assert.Equal(8, roles.Length);
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, true, false, false, true, false)]
    [InlineData(AuthRole.RestaurantAdmin, false, true, false, true, false)]
    [InlineData(AuthRole.BranchManager, false, false, true, true, false)]
    [InlineData(AuthRole.Cashier, false, false, true, true, false)]
    [InlineData(AuthRole.Kitchen, false, false, true, true, false)]
    [InlineData(AuthRole.Bar, false, false, true, true, false)]
    [InlineData(AuthRole.Waiter, false, false, true, true, false)]
    [InlineData(AuthRole.Customer, false, false, false, false, true)]
    public void AuthRoleExtensions_ClassificationPredicates_MatchExpected(
        AuthRole role,
        bool isPlatform,
        bool isTenant,
        bool isBranch,
        bool isStaff,
        bool isCustomer)
    {
        Assert.Equal(isPlatform, role.IsPlatformRole());
        Assert.Equal(isTenant, role.IsTenantRole());
        Assert.Equal(isBranch, role.IsBranchRole());
        Assert.Equal(isStaff, role.IsStaffRole());
        Assert.Equal(isCustomer, role.IsCustomerRole());
        Assert.False(string.IsNullOrWhiteSpace(role.GetTurkishDisplayName()));
    }

    [Fact]
    public void AuthorizationScope_PlatformScope_OnlyCompatibleWithSuperAdmin()
    {
        var platformScope = AuthorizationScope.Platform();

        Assert.Equal(AuthorizationScopeType.Platform, platformScope.ScopeType);
        Assert.Null(platformScope.TenantId);
        Assert.Null(platformScope.BranchId);
        Assert.Null(platformScope.TableSessionId);

        // SuperAdmin succeeds
        platformScope.ValidateRoleCompatibility(AuthRole.SuperAdmin);

        // All other 7 roles must fail
        foreach (var role in Enum.GetValues<AuthRole>())
        {
            if (role != AuthRole.SuperAdmin)
            {
                Assert.Throws<InvalidAuthorizationScopeException>(() =>
                    platformScope.ValidateRoleCompatibility(role));
            }
        }
    }

    [Fact]
    public void AuthorizationScope_TenantScope_OnlyCompatibleWithRestaurantAdmin()
    {
        var tenantScope = AuthorizationScope.ForTenant(_tenantId);

        Assert.Equal(AuthorizationScopeType.Tenant, tenantScope.ScopeType);
        Assert.Equal(_tenantId, tenantScope.TenantId);
        Assert.Null(tenantScope.BranchId);
        Assert.Null(tenantScope.TableSessionId);

        tenantScope.ValidateRoleCompatibility(AuthRole.RestaurantAdmin);

        foreach (var role in Enum.GetValues<AuthRole>())
        {
            if (role != AuthRole.RestaurantAdmin)
            {
                Assert.Throws<InvalidAuthorizationScopeException>(() =>
                    tenantScope.ValidateRoleCompatibility(role));
            }
        }
    }

    [Fact]
    public void AuthorizationScope_BranchScope_OnlyCompatibleWithBranchStaffRoles()
    {
        var branchScope = AuthorizationScope.ForBranch(_tenantId, _branchId);

        Assert.Equal(AuthorizationScopeType.Branch, branchScope.ScopeType);
        Assert.Equal(_tenantId, branchScope.TenantId);
        Assert.Equal(_branchId, branchScope.BranchId);
        Assert.Null(branchScope.TableSessionId);

        var branchRoles = new[]
        {
            AuthRole.BranchManager,
            AuthRole.Cashier,
            AuthRole.Kitchen,
            AuthRole.Bar,
            AuthRole.Waiter
        };

        foreach (var role in branchRoles)
        {
            branchScope.ValidateRoleCompatibility(role);
        }

        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            branchScope.ValidateRoleCompatibility(AuthRole.SuperAdmin));
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            branchScope.ValidateRoleCompatibility(AuthRole.RestaurantAdmin));
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            branchScope.ValidateRoleCompatibility(AuthRole.Customer));
    }

    [Fact]
    public void AuthorizationScope_TableSessionScope_OnlyCompatibleWithCustomer()
    {
        var tableScope = AuthorizationScope.ForTableSession(_tenantId, _branchId, _tableSessionId);

        Assert.Equal(AuthorizationScopeType.TableSession, tableScope.ScopeType);
        Assert.Equal(_tenantId, tableScope.TenantId);
        Assert.Equal(_branchId, tableScope.BranchId);
        Assert.Equal(_tableSessionId, tableScope.TableSessionId);

        tableScope.ValidateRoleCompatibility(AuthRole.Customer);

        foreach (var role in Enum.GetValues<AuthRole>())
        {
            if (role != AuthRole.Customer)
            {
                Assert.Throws<InvalidAuthorizationScopeException>(() =>
                    tableScope.ValidateRoleCompatibility(role));
            }
        }
    }

    [Fact]
    public void AuthorizationScope_ForTableSession_RejectsEmptyGuid()
    {
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            AuthorizationScope.ForTableSession(_tenantId, _branchId, Guid.Empty));
    }

    [Fact]
    public void AuthenticatedPrincipal_CreateStaff_ValidatesAllInvariants()
    {
        var scope = AuthorizationScope.ForBranch(_tenantId, _branchId);
        var principal = AuthenticatedPrincipal.CreateStaff(
            _userId,
            AuthRole.BranchManager,
            scope,
            _sessionId,
            AuthenticationMethod.Password,
            securityVersion: 1);

        Assert.Equal(_userId, principal.SubjectId);
        Assert.Equal(PrincipalType.Staff, principal.PrincipalType);
        Assert.Equal(AuthRole.BranchManager, principal.Role);
        Assert.Equal(_sessionId, principal.SessionId);
        Assert.Equal(AuthenticationMethod.Password, principal.AuthMethod);
        Assert.Equal(1, principal.SecurityVersion);
        Assert.True(principal.IsStaff);
        Assert.False(principal.IsCustomer);
        Assert.False(principal.IsSuperAdmin);
        Assert.Equal(_tenantId, principal.TenantId);
        Assert.Equal(_branchId, principal.BranchId);
        Assert.Null(principal.TableSessionId);
    }

    [Fact]
    public void AuthenticatedPrincipal_CreateCustomer_ValidatesAllInvariants()
    {
        var principal = AuthenticatedPrincipal.CreateCustomer(_tableSessionId, _tenantId, _branchId);

        Assert.Equal(_tableSessionId, principal.SubjectId);
        Assert.Equal(PrincipalType.Customer, principal.PrincipalType);
        Assert.Equal(AuthRole.Customer, principal.Role);
        Assert.Equal(_tableSessionId, principal.SessionId);
        Assert.Equal(AuthenticationMethod.CustomerQrSession, principal.AuthMethod);
        Assert.Equal(0, principal.SecurityVersion);
        Assert.True(principal.IsCustomer);
        Assert.False(principal.IsStaff);
        Assert.False(principal.IsSuperAdmin);
        Assert.Equal(_tenantId, principal.TenantId);
        Assert.Equal(_branchId, principal.BranchId);
        Assert.Equal(_tableSessionId, principal.TableSessionId);
    }

    [Fact]
    public void AuthenticatedPrincipal_RejectsEmptyIdentifiers()
    {
        var scope = AuthorizationScope.ForTenant(_tenantId);

        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            new AuthenticatedPrincipal(
                Guid.Empty,
                PrincipalType.Staff,
                AuthRole.RestaurantAdmin,
                scope,
                _sessionId,
                AuthenticationMethod.Password,
                1));

        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            new AuthenticatedPrincipal(
                _userId,
                PrincipalType.Staff,
                AuthRole.RestaurantAdmin,
                scope,
                Guid.Empty,
                AuthenticationMethod.Password,
                1));
    }

    [Fact]
    public void AuthenticatedPrincipal_StaffRole_RejectsCustomerRoleOrQrAuth()
    {
        var tableScope = AuthorizationScope.ForTableSession(_tenantId, _branchId, _tableSessionId);
        var branchScope = AuthorizationScope.ForBranch(_tenantId, _branchId);

        // Staff cannot have customer role
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            new AuthenticatedPrincipal(
                _userId,
                PrincipalType.Staff,
                AuthRole.Customer,
                tableScope,
                _sessionId,
                AuthenticationMethod.Password,
                1));

        // Staff cannot have CustomerQrSession auth method
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            new AuthenticatedPrincipal(
                _userId,
                PrincipalType.Staff,
                AuthRole.Waiter,
                branchScope,
                _sessionId,
                AuthenticationMethod.CustomerQrSession,
                1));

        // Staff security version must be >= 1
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            new AuthenticatedPrincipal(
                _userId,
                PrincipalType.Staff,
                AuthRole.Waiter,
                branchScope,
                _sessionId,
                AuthenticationMethod.Password,
                securityVersion: 0));
    }

    [Fact]
    public void AuthenticatedPrincipal_PinAuth_RestrictedStrictlyToBranchStaff()
    {
        var platformScope = AuthorizationScope.Platform();
        var tenantScope = AuthorizationScope.ForTenant(_tenantId);
        var branchScope = AuthorizationScope.ForBranch(_tenantId, _branchId);

        // SuperAdmin cannot authenticate via PIN
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            new AuthenticatedPrincipal(
                _userId,
                PrincipalType.Staff,
                AuthRole.SuperAdmin,
                platformScope,
                _sessionId,
                AuthenticationMethod.Pin,
                1));

        // RestaurantAdmin cannot authenticate via PIN
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            new AuthenticatedPrincipal(
                _userId,
                PrincipalType.Staff,
                AuthRole.RestaurantAdmin,
                tenantScope,
                _sessionId,
                AuthenticationMethod.Pin,
                1));

        // Branch staff can authenticate via PIN
        var waiter = new AuthenticatedPrincipal(
            _userId,
            PrincipalType.Staff,
            AuthRole.Waiter,
            branchScope,
            _sessionId,
            AuthenticationMethod.Pin,
            1);

        Assert.Equal(AuthenticationMethod.Pin, waiter.AuthMethod);
    }
}
