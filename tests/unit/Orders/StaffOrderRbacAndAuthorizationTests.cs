using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class StaffOrderRbacAndAuthorizationTests
{
    private readonly IPermissionRegistry _registry = new PermissionRegistry();
    private static readonly TenantId SampleTenantId = new(Guid.NewGuid());
    private static readonly BranchId SampleBranchId = new(Guid.NewGuid());

    [Theory]
    [InlineData(AuthRole.SuperAdmin, false)]
    [InlineData(AuthRole.RestaurantAdmin, true)]
    [InlineData(AuthRole.BranchManager, true)]
    [InlineData(AuthRole.Cashier, true)]
    [InlineData(AuthRole.Kitchen, false)]
    [InlineData(AuthRole.Bar, false)]
    [InlineData(AuthRole.Waiter, true)]
    [InlineData(AuthRole.Customer, false)]
    public void PermissionRegistry_OrdersStaffCreate_MatchesRbacMatrix(AuthRole role, bool expectedPermitted)
    {
        var principal = CreatePrincipalForRole(role);
        var result = _registry.IsPermitted(principal, Permissions.OrdersStaffCreate);
        Assert.Equal(expectedPermitted, result);
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, false)]
    [InlineData(AuthRole.RestaurantAdmin, true)]
    [InlineData(AuthRole.BranchManager, true)]
    [InlineData(AuthRole.Cashier, true)]
    [InlineData(AuthRole.Kitchen, false)]
    [InlineData(AuthRole.Bar, false)]
    [InlineData(AuthRole.Waiter, true)]
    [InlineData(AuthRole.Customer, false)]
    public void PermissionRegistry_OrdersItemsCancelPrePrep_MatchesRbacMatrix(AuthRole role, bool expectedPermitted)
    {
        var principal = CreatePrincipalForRole(role);
        var result = _registry.IsPermitted(principal, Permissions.OrdersItemsCancelPrePrep);
        Assert.Equal(expectedPermitted, result);
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, false)]
    [InlineData(AuthRole.RestaurantAdmin, true)]
    [InlineData(AuthRole.BranchManager, true)]
    [InlineData(AuthRole.Cashier, false)]
    [InlineData(AuthRole.Kitchen, false)]
    [InlineData(AuthRole.Bar, false)]
    [InlineData(AuthRole.Waiter, false)]
    [InlineData(AuthRole.Customer, false)]
    public void PermissionRegistry_OrdersItemsVoidInPrep_MatchesRbacMatrix(AuthRole role, bool expectedPermitted)
    {
        var principal = CreatePrincipalForRole(role);
        var result = _registry.IsPermitted(principal, Permissions.OrdersItemsVoidInPrep);
        Assert.Equal(expectedPermitted, result);
    }

    [Fact]
    public void CustomerPrincipal_IsDeniedStaffOrderCreation()
    {
        var customer = AuthenticatedPrincipal.CreateCustomer(Guid.NewGuid(), SampleTenantId, SampleBranchId);
        Assert.False(_registry.IsPermitted(customer, Permissions.OrdersStaffCreate));
        Assert.False(_registry.IsPermitted(customer, Permissions.OrdersItemsCancelPrePrep));
        Assert.False(_registry.IsPermitted(customer, Permissions.OrdersItemsVoidInPrep));
    }

    private static AuthenticatedPrincipal CreatePrincipalForRole(AuthRole role)
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        if (role == AuthRole.Customer)
        {
            return AuthenticatedPrincipal.CreateCustomer(sessionId, SampleTenantId, SampleBranchId);
        }

        if (role == AuthRole.SuperAdmin)
        {
            return AuthenticatedPrincipal.CreateStaff(
                userId,
                role,
                AuthorizationScope.Platform(),
                sessionId,
                AuthenticationMethod.Password,
                1);
        }

        if (role == AuthRole.RestaurantAdmin)
        {
            return AuthenticatedPrincipal.CreateStaff(
                userId,
                role,
                AuthorizationScope.ForTenant(SampleTenantId),
                sessionId,
                AuthenticationMethod.Password,
                1);
        }

        return AuthenticatedPrincipal.CreateStaff(
            userId,
            role,
            AuthorizationScope.ForBranch(SampleTenantId, SampleBranchId),
            sessionId,
            AuthenticationMethod.Pin,
            1);
    }
}
