using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class FloorSessionRbacUnitTests
{
    private readonly IPermissionRegistry _registry = new PermissionRegistry();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();

    private FloorService CreateService(TenantId? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=postgres;Password=dummy")
            .Options;
        var tId = tenantId ?? _tenantId;
        var dbContext = new RestaurantOrderDbContext(options, new TenantContext(tId.Value, isAuthenticated: true));
        return new FloorService(dbContext, NullLogger<FloorService>.Instance, _registry);
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, PermissionGrantType.Denied)]
    [InlineData(AuthRole.RestaurantAdmin, PermissionGrantType.Full)]
    [InlineData(AuthRole.BranchManager, PermissionGrantType.Full)]
    [InlineData(AuthRole.Cashier, PermissionGrantType.Full)]
    [InlineData(AuthRole.Kitchen, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Bar, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Waiter, PermissionGrantType.Full)]
    [InlineData(AuthRole.Customer, PermissionGrantType.OwnOrAssigned)]
    public void FloorSessionsManage_EightRoleMatrix_EnforcesStrictAccess(
        AuthRole role,
        PermissionGrantType expectedGrant)
    {
        var grant = _registry.GetGrantType(role, Permissions.FloorSessionsManage);
        Assert.Equal(expectedGrant, grant);

        var hasFull = _registry.HasFullGrant(role, Permissions.FloorSessionsManage);
        Assert.Equal(expectedGrant == PermissionGrantType.Full, hasFull);
    }

    [Fact]
    public void Customer_OwnSessionOwnership_EvaluatesCorrectly()
    {
        var sessionId = Guid.NewGuid();
        var customer = AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: sessionId,
            tenantId: _tenantId,
            branchId: _branchId);

        var ownContext = ResourceOwnershipContext.ForTableSession(sessionId);

        Assert.True(_registry.IsPermitted(customer, Permissions.FloorSessionsManage, ownContext));

        var foreignContext = ResourceOwnershipContext.ForTableSession(Guid.NewGuid()); // different session

        Assert.False(_registry.IsPermitted(customer, Permissions.FloorSessionsManage, foreignContext));
    }

    [Fact]
    public async Task OpenSession_SuperAdmin_ThrowsInvalidAuthorizationScopeException()
    {
        var service = CreateService();
        var superAdmin = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.SuperAdmin,
            scope: AuthorizationScope.Platform(),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        var request = new OpenDiningSessionRequest(2);

        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.OpenSessionAsync(_tenantId, _branchId, RestaurantTableId.New(), request, superAdmin));

        Assert.Contains("SuperAdmin", ex.Message);
    }

    [Fact]
    public async Task OpenSession_CrossTenantActor_ThrowsInvalidAuthorizationScopeException()
    {
        var service = CreateService();
        var otherTenantId = TenantId.New();
        var actor = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(otherTenantId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        var request = new OpenDiningSessionRequest(2);

        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.OpenSessionAsync(_tenantId, _branchId, RestaurantTableId.New(), request, actor));

        Assert.Contains("authorized for this tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenSession_CrossBranchActor_ThrowsInvalidAuthorizationScopeException()
    {
        var service = CreateService();
        var otherBranchId = BranchId.New();
        var actor = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.BranchManager,
            scope: AuthorizationScope.ForBranch(_tenantId, otherBranchId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        var request = new OpenDiningSessionRequest(2);

        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.OpenSessionAsync(_tenantId, _branchId, RestaurantTableId.New(), request, actor));

        Assert.Contains("authorized to access branch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloseSession_CustomerActor_ThrowsInvalidAuthorizationScopeException()
    {
        var service = CreateService();
        var customer = AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: Guid.NewGuid(),
            tenantId: _tenantId,
            branchId: _branchId);

        var request = new CloseDiningSessionRequest("Paid");

        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.CloseSessionAsync(
                _tenantId,
                _branchId,
                DiningSessionId.New(),
                request,
                Guid.NewGuid(),
                customer));

        Assert.Contains("authorized to close dining sessions", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestBill_CustomerForeignSession_ThrowsInvalidAuthorizationScopeException()
    {
        var service = CreateService();
        var customer = AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: Guid.NewGuid(), // session A
            tenantId: _tenantId,
            branchId: _branchId);

        var targetSessionId = DiningSessionId.New(); // session B

        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.RequestBillAsync(
                _tenantId,
                _branchId,
                targetSessionId,
                Guid.NewGuid(),
                customer));

        Assert.Contains("foreign dining session", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
