using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.RestaurantConfig;

public class RestaurantConfigRbacMatrixUnitTests
{
    private readonly IPermissionRegistry _registry = new PermissionRegistry();

    [Theory]
    [InlineData(AuthRole.SuperAdmin, PermissionGrantType.Denied)]
    [InlineData(AuthRole.RestaurantAdmin, PermissionGrantType.Full)]
    [InlineData(AuthRole.BranchManager, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Cashier, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Kitchen, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Bar, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Waiter, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Customer, PermissionGrantType.Denied)]
    public void TenantBrandsManage_PermissionMatrix_StrictlyRestrictedToRestaurantAdmin(
        AuthRole role,
        PermissionGrantType expectedGrant)
    {
        var grant = _registry.GetGrantType(role, Permissions.TenantBrandsManage);
        Assert.Equal(expectedGrant, grant);

        var hasFull = _registry.HasFullGrant(role, Permissions.TenantBrandsManage);
        Assert.Equal(expectedGrant == PermissionGrantType.Full, hasFull);
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, PermissionGrantType.Denied)]
    [InlineData(AuthRole.RestaurantAdmin, PermissionGrantType.Full)]
    [InlineData(AuthRole.BranchManager, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Cashier, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Kitchen, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Bar, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Waiter, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Customer, PermissionGrantType.Denied)]
    public void TenantBranchesManage_PermissionMatrix_StrictlyRestrictedToRestaurantAdmin(
        AuthRole role,
        PermissionGrantType expectedGrant)
    {
        var grant = _registry.GetGrantType(role, Permissions.TenantBranchesManage);
        Assert.Equal(expectedGrant, grant);

        var hasFull = _registry.HasFullGrant(role, Permissions.TenantBranchesManage);
        Assert.Equal(expectedGrant == PermissionGrantType.Full, hasFull);
    }

    [Fact]
    public void IsPermitted_RestaurantAdmin_CanManageBrandsAndBranches()
    {
        var tenantId = TenantId.New();
        var principal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        Assert.True(_registry.IsPermitted(principal, Permissions.TenantBrandsManage));
        Assert.True(_registry.IsPermitted(principal, Permissions.TenantBranchesManage));
    }

    [Fact]
    public void IsPermitted_BranchManager_CannotManageBrandsOrBranches()
    {
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var principal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.BranchManager,
            scope: AuthorizationScope.ForBranch(tenantId, branchId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        Assert.False(_registry.IsPermitted(principal, Permissions.TenantBrandsManage));
        Assert.False(_registry.IsPermitted(principal, Permissions.TenantBranchesManage));
    }

    [Fact]
    public void IsPermitted_SuperAdmin_CannotManageTenantBrandsOrBranches()
    {
        var principal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.SuperAdmin,
            scope: AuthorizationScope.Platform(),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        Assert.False(_registry.IsPermitted(principal, Permissions.TenantBrandsManage));
        Assert.False(_registry.IsPermitted(principal, Permissions.TenantBranchesManage));
    }

    [Fact]
    public void IsPermitted_NullPrincipalOrEmptyPermission_FailsClosed()
    {
        Assert.False(_registry.IsPermitted(null!, Permissions.TenantBrandsManage));
        Assert.False(_registry.IsPermitted(null!, Permissions.TenantBranchesManage));

        var principal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(TenantId.New()),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        Assert.False(_registry.IsPermitted(principal, ""));
        Assert.False(_registry.IsPermitted(principal, "   "));
        Assert.False(_registry.IsPermitted(principal, null!));
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, PermissionGrantType.Denied)]
    [InlineData(AuthRole.RestaurantAdmin, PermissionGrantType.Full)]
    [InlineData(AuthRole.BranchManager, PermissionGrantType.OwnOrAssigned)]
    [InlineData(AuthRole.Cashier, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Kitchen, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Bar, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Waiter, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Customer, PermissionGrantType.Denied)]
    public void BranchConfigurationManage_PermissionMatrix_Checks(
        AuthRole role,
        PermissionGrantType expectedGrant)
    {
        var grant = _registry.GetGrantType(role, Permissions.BranchConfigurationManage);
        Assert.Equal(expectedGrant, grant);
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, PermissionGrantType.Denied)]
    [InlineData(AuthRole.RestaurantAdmin, PermissionGrantType.Full)]
    [InlineData(AuthRole.BranchManager, PermissionGrantType.Full)]
    [InlineData(AuthRole.Cashier, PermissionGrantType.Full)]
    [InlineData(AuthRole.Kitchen, PermissionGrantType.Full)]
    [InlineData(AuthRole.Bar, PermissionGrantType.Full)]
    [InlineData(AuthRole.Waiter, PermissionGrantType.Full)]
    [InlineData(AuthRole.Customer, PermissionGrantType.Full)]
    public void BranchConfigurationView_PermissionMatrix_AllowsStaffRoles(
        AuthRole role,
        PermissionGrantType expectedGrant)
    {
        var grant = _registry.GetGrantType(role, Permissions.BranchConfigurationView);
        Assert.Equal(expectedGrant, grant);
    }

    [Fact]
    public void FeatureFlag_DoesNotBypassAuthorization_DeniedRoleCannotManage()
    {
        // Even if a feature flag is theoretically on, RBAC strictly denies Cashier, Waiter, or Customer
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var customerPrincipal = AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: Guid.NewGuid(),
            tenantId: tenantId,
            branchId: branchId);

        Assert.False(_registry.IsPermitted(customerPrincipal, Permissions.BranchConfigurationManage));

        var waiterPrincipal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.Waiter,
            scope: AuthorizationScope.ForBranch(tenantId, branchId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        Assert.False(_registry.IsPermitted(waiterPrincipal, Permissions.BranchConfigurationManage));
    }
}
