using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class FloorRbacMatrixUnitTests
{
    private readonly IPermissionRegistry _registry = new PermissionRegistry();

    [Theory]
    [InlineData(AuthRole.SuperAdmin, PermissionGrantType.Denied)]
    [InlineData(AuthRole.RestaurantAdmin, PermissionGrantType.Full)]
    [InlineData(AuthRole.BranchManager, PermissionGrantType.Full)]
    [InlineData(AuthRole.Cashier, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Kitchen, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Bar, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Waiter, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Customer, PermissionGrantType.Denied)]
    public void BranchTablesManage_EightRoleMatrix_EnforcesStrictAccess(
        AuthRole role,
        PermissionGrantType expectedGrant)
    {
        var grant = _registry.GetGrantType(role, Permissions.BranchTablesManage);
        Assert.Equal(expectedGrant, grant);

        var hasFull = _registry.HasFullGrant(role, Permissions.BranchTablesManage);
        Assert.Equal(expectedGrant == PermissionGrantType.Full, hasFull);
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, PermissionGrantType.Denied)]
    [InlineData(AuthRole.RestaurantAdmin, PermissionGrantType.Full)]
    [InlineData(AuthRole.BranchManager, PermissionGrantType.Full)]
    [InlineData(AuthRole.Cashier, PermissionGrantType.Full)]
    [InlineData(AuthRole.Kitchen, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Bar, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Waiter, PermissionGrantType.Full)]
    [InlineData(AuthRole.Customer, PermissionGrantType.Denied)]
    public void FloorStatusView_EightRoleMatrix_EnforcesStrictAccess(
        AuthRole role,
        PermissionGrantType expectedGrant)
    {
        var grant = _registry.GetGrantType(role, Permissions.FloorStatusView);
        Assert.Equal(expectedGrant, grant);

        var hasFull = _registry.HasFullGrant(role, Permissions.FloorStatusView);
        Assert.Equal(expectedGrant == PermissionGrantType.Full, hasFull);
    }

    [Fact]
    public void IsPermitted_RestaurantAdmin_CanManageTablesInOwnTenant()
    {
        var tenantId = TenantId.New();
        var principal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        Assert.True(_registry.IsPermitted(principal, Permissions.BranchTablesManage));
        Assert.True(_registry.IsPermitted(principal, Permissions.FloorStatusView));
    }

    [Fact]
    public void IsPermitted_BranchManager_CanManageTablesInOwnBranch()
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

        Assert.True(_registry.IsPermitted(principal, Permissions.BranchTablesManage));
        Assert.True(_registry.IsPermitted(principal, Permissions.FloorStatusView));
    }

    [Fact]
    public void IsPermitted_WaiterAndCashier_CanViewFloorStatus_CannotManageTables()
    {
        var tenantId = TenantId.New();
        var branchId = BranchId.New();

        var waiter = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.Waiter,
            scope: AuthorizationScope.ForBranch(tenantId, branchId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        var cashier = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.Cashier,
            scope: AuthorizationScope.ForBranch(tenantId, branchId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        Assert.True(_registry.IsPermitted(waiter, Permissions.FloorStatusView));
        Assert.False(_registry.IsPermitted(waiter, Permissions.BranchTablesManage));

        Assert.True(_registry.IsPermitted(cashier, Permissions.FloorStatusView));
        Assert.False(_registry.IsPermitted(cashier, Permissions.BranchTablesManage));
    }

    [Fact]
    public void IsPermitted_SuperAdmin_CannotDirectlyManageBranchTables()
    {
        var principal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.SuperAdmin,
            scope: AuthorizationScope.Platform(),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        Assert.False(_registry.IsPermitted(principal, Permissions.BranchTablesManage));
        Assert.False(_registry.IsPermitted(principal, Permissions.FloorStatusView));
    }

    [Fact]
    public void IsPermitted_KitchenBarCustomer_CannotManageTablesOrViewFloor()
    {
        var tenantId = TenantId.New();
        var branchId = BranchId.New();

        foreach (var role in new[] { AuthRole.Kitchen, AuthRole.Bar })
        {
            var staff = AuthenticatedPrincipal.CreateStaff(
                userId: Guid.NewGuid(),
                role: role,
                scope: AuthorizationScope.ForBranch(tenantId, branchId),
                sessionId: Guid.NewGuid(),
                authMethod: AuthenticationMethod.Password,
                securityVersion: 1);

            Assert.False(_registry.IsPermitted(staff, Permissions.BranchTablesManage));
            Assert.False(_registry.IsPermitted(staff, Permissions.FloorStatusView));
        }

        var customer = AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: Guid.NewGuid(),
            tenantId: tenantId,
            branchId: branchId);

        Assert.False(_registry.IsPermitted(customer, Permissions.BranchTablesManage));
        Assert.False(_registry.IsPermitted(customer, Permissions.FloorStatusView));
    }
}
