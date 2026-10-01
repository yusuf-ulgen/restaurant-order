using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class ResourceOwnershipRequirementTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly Guid _tableSessionId = Guid.NewGuid();
    private readonly Guid _otherTableSessionId = Guid.NewGuid();

    private readonly IResourceOwnershipRequirement _requirement = new DefaultResourceOwnershipRequirement();
    private readonly IPermissionRegistry _registry = new PermissionRegistry();

    [Fact]
    public void Customer_FloorSessionsManage_AllowedOnlyForOwnTableSession()
    {
        var customer = AuthenticatedPrincipal.CreateCustomer(_tableSessionId, _tenantId, _branchId);

        var ownContext = ResourceOwnershipContext.ForTableSession(_tableSessionId);
        var foreignContext = ResourceOwnershipContext.ForTableSession(_otherTableSessionId);
        var emptyContext = new ResourceOwnershipContext();

        Assert.True(_requirement.Satisfies(customer, Permissions.FloorSessionsManage, ownContext));
        Assert.False(_requirement.Satisfies(customer, Permissions.FloorSessionsManage, foreignContext));
        Assert.False(_requirement.Satisfies(customer, Permissions.FloorSessionsManage, emptyContext));

        // Registry integration
        Assert.True(_registry.IsPermitted(customer, Permissions.FloorSessionsManage, ownContext));
        Assert.False(_registry.IsPermitted(customer, Permissions.FloorSessionsManage, foreignContext));
        Assert.False(_registry.IsPermitted(customer, Permissions.FloorSessionsManage, null));
    }

    [Fact]
    public void Waiter_BillingPaymentPosCard_AllowedOnlyForAssignedOrOwnedStaff()
    {
        var scope = AuthorizationScope.ForBranch(_tenantId, _branchId);
        var waiter = AuthenticatedPrincipal.CreateStaff(
            _userId,
            AuthRole.Waiter,
            scope,
            Guid.NewGuid(),
            AuthenticationMethod.Pin,
            1);

        var assignedContext = ResourceOwnershipContext.ForStaffAssignment(_userId);
        var ownedContext = ResourceOwnershipContext.ForStaffOwnership(_userId);
        var foreignContext = ResourceOwnershipContext.ForStaffAssignment(_otherUserId);
        var emptyContext = new ResourceOwnershipContext();

        Assert.True(_requirement.Satisfies(waiter, Permissions.BillingPaymentPosCard, assignedContext));
        Assert.True(_requirement.Satisfies(waiter, Permissions.BillingPaymentPosCard, ownedContext));
        Assert.False(_requirement.Satisfies(waiter, Permissions.BillingPaymentPosCard, foreignContext));
        Assert.False(_requirement.Satisfies(waiter, Permissions.BillingPaymentPosCard, emptyContext));

        // Registry integration: null context MUST deny (no auto-grant)
        Assert.False(_registry.IsPermitted(waiter, Permissions.BillingPaymentPosCard, null));
        Assert.True(_registry.IsPermitted(waiter, Permissions.BillingPaymentPosCard, assignedContext));
        Assert.False(_registry.IsPermitted(waiter, Permissions.BillingPaymentPosCard, foreignContext));
    }

    [Fact]
    public void Cashier_ReportsBranchRevenue_AllowedOnlyForOwnRegisterShift()
    {
        var scope = AuthorizationScope.ForBranch(_tenantId, _branchId);
        var cashier = AuthenticatedPrincipal.CreateStaff(
            _userId,
            AuthRole.Cashier,
            scope,
            Guid.NewGuid(),
            AuthenticationMethod.Password,
            1);

        var ownShiftContext = ResourceOwnershipContext.ForCashierRegister(_userId, Guid.NewGuid());
        var otherShiftContext = ResourceOwnershipContext.ForCashierRegister(_otherUserId, Guid.NewGuid());

        Assert.True(_requirement.Satisfies(cashier, Permissions.ReportsBranchRevenue, ownShiftContext));
        Assert.False(_requirement.Satisfies(cashier, Permissions.ReportsBranchRevenue, otherShiftContext));

        // Registry integration
        Assert.False(_registry.IsPermitted(cashier, Permissions.ReportsBranchRevenue, null));
        Assert.True(_registry.IsPermitted(cashier, Permissions.ReportsBranchRevenue, ownShiftContext));
        Assert.False(_registry.IsPermitted(cashier, Permissions.ReportsBranchRevenue, otherShiftContext));
    }

    [Theory]
    [InlineData(AuthRole.Kitchen, "kitchen", true)]
    [InlineData(AuthRole.Kitchen, "bar", false)]
    [InlineData(AuthRole.Bar, "bar", true)]
    [InlineData(AuthRole.Bar, "kitchen", false)]
    public void StationStaff_KdsTicketOperations_AllowedOnlyForMatchingStation(
        AuthRole stationRole,
        string ticketStation,
        bool shouldSucceed)
    {
        var scope = AuthorizationScope.ForBranch(_tenantId, _branchId);
        var stationPrincipal = AuthenticatedPrincipal.CreateStaff(
            _userId,
            stationRole,
            scope,
            Guid.NewGuid(),
            AuthenticationMethod.Password,
            1);

        var stationContext = ResourceOwnershipContext.ForStation(ticketStation);

        Assert.Equal(
            shouldSucceed,
            _requirement.Satisfies(stationPrincipal, Permissions.KdsTicketUpdate, stationContext));
        Assert.Equal(
            shouldSucceed,
            _requirement.Satisfies(stationPrincipal, Permissions.KdsTicketRecall, stationContext));

        // Registry integration
        Assert.Equal(
            shouldSucceed,
            _registry.IsPermitted(stationPrincipal, Permissions.KdsTicketUpdate, stationContext));
        Assert.False(
            _registry.IsPermitted(stationPrincipal, Permissions.KdsTicketUpdate, null));
    }

    [Fact]
    public void PermissionRegistry_FullGrant_PermittedEvenWithoutResourceContext()
    {
        var scope = AuthorizationScope.ForBranch(_tenantId, _branchId);
        var manager = AuthenticatedPrincipal.CreateStaff(
            _userId,
            AuthRole.BranchManager,
            scope,
            Guid.NewGuid(),
            AuthenticationMethod.Password,
            1);

        // BranchManager has Full grant for MenuCatalogManage
        Assert.True(_registry.IsPermitted(manager, Permissions.MenuCatalogManage, null));
        Assert.True(_registry.IsPermitted(manager, Permissions.MenuCatalogManage, new ResourceOwnershipContext()));
    }

    [Fact]
    public void PermissionRegistry_DeniedGrant_NeverPermittedEvenWithMatchingContext()
    {
        var scope = AuthorizationScope.ForBranch(_tenantId, _branchId);
        var waiter = AuthenticatedPrincipal.CreateStaff(
            _userId,
            AuthRole.Waiter,
            scope,
            Guid.NewGuid(),
            AuthenticationMethod.Password,
            1);

        // Waiter is strictly prohibited from PlatformTenantsManage and BillingDiscountApply
        var context = ResourceOwnershipContext.ForStaffOwnership(_userId);
        Assert.False(_registry.IsPermitted(waiter, Permissions.PlatformTenantsManage, context));
        Assert.False(_registry.IsPermitted(waiter, Permissions.BillingDiscountApply, context));
    }
}
