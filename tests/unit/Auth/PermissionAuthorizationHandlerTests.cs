using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class PermissionAuthorizationHandlerTests
{
    private readonly PermissionRegistry _registry = new(new DefaultResourceOwnershipRequirement());
    private readonly JwtClaimPrincipalParser _parser = new();
    private readonly PermissionAuthorizationHandler _handler;

    public PermissionAuthorizationHandlerTests()
    {
        _handler = new PermissionAuthorizationHandler(_registry, _parser, new DefaultResourceOwnershipRequirement());
    }

    private static ClaimsPrincipal CreateClaimsPrincipal(
        AuthRole role,
        TenantId? tenantId = null,
        BranchId? branchId = null,
        int securityVersion = 1)
    {
        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new(JwtClaimNames.SessionId, Guid.NewGuid().ToString()),
            new(JwtClaimNames.PrincipalType, "staff"),
            new(JwtClaimNames.Role, role.ToString()),
            new(JwtClaimNames.AuthMethod, "password"),
            new(JwtClaimNames.SecurityVersion, securityVersion.ToString())
        };

        if (tenantId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.TenantId, tenantId.Value.Value.ToString()));
        }

        if (branchId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.BranchId, branchId.Value.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task HandleAsync_UnauthenticatedUser_DoesNotSucceed()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // IsAuthenticated = false
        var requirement = new PermissionRequirement(Permissions.MenuCatalogManage);
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_RoleWithFullGrant_Succeeds()
    {
        var tenantId = TenantId.New();
        var user = CreateClaimsPrincipal(AuthRole.RestaurantAdmin, tenantId);
        var requirement = new PermissionRequirement(Permissions.MenuCatalogManage);
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_RoleWithDeniedGrant_Fails()
    {
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var user = CreateClaimsPrincipal(AuthRole.Waiter, tenantId, branchId);
        var requirement = new PermissionRequirement(Permissions.PlatformTenantsManage);
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task HandleAsync_UnknownPermission_DeniesByDefault()
    {
        var tenantId = TenantId.New();
        var user = CreateClaimsPrincipal(AuthRole.RestaurantAdmin, tenantId);
        var requirement = new PermissionRequirement("unknown.nonexistent.permission");
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task HandleAsync_OwnOrAssigned_WhenResourceMatches_Succeeds()
    {
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var waiterPrincipal = CreateClaimsPrincipal(AuthRole.Waiter, tenantId, branchId);

        var subGuid = Guid.Parse(waiterPrincipal.FindFirst(JwtClaimNames.Subject)!.Value);
        var resourceContext = new ResourceOwnershipContext
        {
            AssignedStaffId = subGuid
        };

        var requirement = new PermissionRequirement(Permissions.BillingPaymentPosCard);
        var context = new AuthorizationHandlerContext([requirement], waiterPrincipal, resourceContext);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_OwnOrAssigned_WhenAssignedToDifferentUser_Fails()
    {
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var waiterPrincipal = CreateClaimsPrincipal(AuthRole.Waiter, tenantId, branchId);

        var differentUserGuid = Guid.NewGuid();
        var resourceContext = new ResourceOwnershipContext
        {
            AssignedStaffId = differentUserGuid
        };

        var requirement = new PermissionRequirement(Permissions.BillingPaymentPosCard);
        var context = new AuthorizationHandlerContext([requirement], waiterPrincipal, resourceContext);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }
}
