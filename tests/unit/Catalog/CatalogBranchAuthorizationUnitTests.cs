using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Catalog;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class CatalogBranchAuthorizationUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branch1 = BranchId.New();
    private readonly BranchId _branch2 = BranchId.New();
    private readonly TenantId _otherTenantId = TenantId.New();

    private CatalogService CreateService()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=postgres;Password=dummy")
            .Options;
        var dbContext = new RestaurantOrderDbContext(options, new TenantContext(_tenantId.Value, isAuthenticated: true));
        return new CatalogService(dbContext, NullLogger<CatalogService>.Instance);
    }

    private static AuthenticatedPrincipal CreateActor(
        AuthRole role,
        TenantId? tenantId,
        BranchId? branchId = null)
    {
        AuthorizationScope scope;
        if (role == AuthRole.Customer && tenantId.HasValue && branchId.HasValue)
        {
            scope = AuthorizationScope.ForTableSession(tenantId.Value, branchId.Value, Guid.NewGuid());
        }
        else if (role == AuthRole.SuperAdmin)
        {
            scope = AuthorizationScope.Platform();
        }
        else if (role == AuthRole.RestaurantAdmin && tenantId.HasValue)
        {
            scope = AuthorizationScope.ForTenant(tenantId.Value);
        }
        else if (tenantId.HasValue && branchId.HasValue)
        {
            scope = AuthorizationScope.ForBranch(tenantId.Value, branchId.Value);
        }
        else
        {
            scope = AuthorizationScope.Platform();
        }

        return new AuthenticatedPrincipal(
            subjectId: Guid.NewGuid(),
            principalType: role == AuthRole.Customer ? PrincipalType.Customer : PrincipalType.Staff,
            role: role,
            scope: scope,
            sessionId: Guid.NewGuid(),
            authMethod: role == AuthRole.Customer ? AuthenticationMethod.CustomerQrSession : AuthenticationMethod.Password,
            securityVersion: role == AuthRole.Customer ? 0 : 1);
    }

    [Fact]
    public async Task RestaurantAdmin_CanAccessAnyBranch_WithinSameTenant()
    {
        var service = CreateService();
        var admin = CreateActor(AuthRole.RestaurantAdmin, _tenantId);

        // Does not throw InvalidAuthorizationScopeException when checking branch1 or branch2
        // (It proceeds to DB query, which will fail connection or return, but NOT auth scope exception)
        var ex1 = await Record.ExceptionAsync(() => service.ListMenusAsync(_tenantId, _branch1, admin, CancellationToken.None));
        Assert.False(ex1 is InvalidAuthorizationScopeException);

        var ex2 = await Record.ExceptionAsync(() => service.ListMenusAsync(_tenantId, _branch2, admin, CancellationToken.None));
        Assert.False(ex2 is InvalidAuthorizationScopeException);
    }

    [Fact]
    public async Task RestaurantAdmin_Rejected_WhenAccessingOtherTenantBranch()
    {
        var service = CreateService();
        var admin = CreateActor(AuthRole.RestaurantAdmin, _tenantId);

        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.ListMenusAsync(_otherTenantId, _branch1, admin, CancellationToken.None));

        Assert.Contains("not authorized for this tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AuthRole.BranchManager)]
    [InlineData(AuthRole.Cashier)]
    [InlineData(AuthRole.Kitchen)]
    [InlineData(AuthRole.Bar)]
    [InlineData(AuthRole.Waiter)]
    [InlineData(AuthRole.Customer)]
    public async Task Role_CrossBranchAccess_StrictlyRejected_WithInvalidAuthorizationScopeException(AuthRole role)
    {
        var service = CreateService();
        // Actor is scoped strictly to branch 1
        var actor = CreateActor(role, _tenantId, _branch1);

        // Attempting to access branch 2 must fail-closed immediately
        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.ListMenusAsync(_tenantId, _branch2, actor, CancellationToken.None));

        Assert.Contains("is not authorized to access branch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AuthRole.BranchManager)]
    [InlineData(AuthRole.Cashier)]
    [InlineData(AuthRole.Kitchen)]
    [InlineData(AuthRole.Bar)]
    [InlineData(AuthRole.Waiter)]
    [InlineData(AuthRole.Customer)]
    public async Task Role_OwnBranchAccess_PassesBranchSecurityCheck(AuthRole role)
    {
        var service = CreateService();
        var actor = CreateActor(role, _tenantId, _branch1);

        var ex = await Record.ExceptionAsync(() => service.ListMenusAsync(_tenantId, _branch1, actor, CancellationToken.None));
        Assert.False(ex is InvalidAuthorizationScopeException);
    }

    [Fact]
    public void BranchRole_CannotBeConstructedWithoutBranchScope()
    {
        var tenantScope = AuthorizationScope.ForTenant(_tenantId);

        var ex = Assert.Throws<InvalidAuthorizationScopeException>(() =>
            new AuthenticatedPrincipal(
                subjectId: Guid.NewGuid(),
                principalType: PrincipalType.Staff,
                role: AuthRole.Kitchen,
                scope: tenantScope,
                sessionId: Guid.NewGuid(),
                authMethod: AuthenticationMethod.Password,
                securityVersion: 1));

        Assert.Contains("Role 'Kitchen' is not permitted in Tenant scope", ex.Message);
    }

    [Fact]
    public async Task SuperAdmin_CannotBypassTenantIsolation()
    {
        var service = CreateService();
        // SuperAdmin with no tenant scope
        var superAdminNoScope = CreateActor(AuthRole.SuperAdmin, tenantId: null);

        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.ListMenusAsync(_tenantId, _branch1, superAdminNoScope, CancellationToken.None));

        Assert.Contains("SuperAdmin is not authorized to bypass tenant scope", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
