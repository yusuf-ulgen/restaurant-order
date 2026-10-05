using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Catalog;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class CatalogServiceMutationPermissionUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branch1 = BranchId.New();
    private readonly BranchId _branch2 = BranchId.New();

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

    [Theory]
    [InlineData(AuthRole.Cashier)]
    [InlineData(AuthRole.Kitchen)]
    [InlineData(AuthRole.Bar)]
    [InlineData(AuthRole.Waiter)]
    [InlineData(AuthRole.Customer)]
    public async Task UnauthorizedRoles_CannotCreateMenu_ThrowsInvalidAuthorizationScopeException(AuthRole role)
    {
        var service = CreateService();
        var actor = CreateActor(role, _tenantId, _branch1);

        var cmd = new CreateMenuCommand("Test Menu", "test-menu");
        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.CreateMenuAsync(_tenantId, _branch1, cmd, actor, CancellationToken.None));

        Assert.Contains("Actor is not authorized to manage catalog", ex.Message);
    }

    [Theory]
    [InlineData(AuthRole.Cashier)]
    [InlineData(AuthRole.Kitchen)]
    [InlineData(AuthRole.Bar)]
    [InlineData(AuthRole.Waiter)]
    [InlineData(AuthRole.Customer)]
    public async Task UnauthorizedRoles_CannotCreateCategory_ThrowsInvalidAuthorizationScopeException(AuthRole role)
    {
        var service = CreateService();
        var actor = CreateActor(role, _tenantId, _branch1);

        var cmd = new CreateMenuCategoryCommand("Cat", "cat");
        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.CreateCategoryAsync(_tenantId, _branch1, MenuId.New(), cmd, actor, CancellationToken.None));

        Assert.Contains("Actor is not authorized to manage catalog", ex.Message);
    }

    [Theory]
    [InlineData(AuthRole.Cashier)]
    [InlineData(AuthRole.Kitchen)]
    [InlineData(AuthRole.Bar)]
    [InlineData(AuthRole.Waiter)]
    [InlineData(AuthRole.Customer)]
    public async Task UnauthorizedRoles_CannotReorderCategories_ThrowsInvalidAuthorizationScopeException(AuthRole role)
    {
        var service = CreateService();
        var actor = CreateActor(role, _tenantId, _branch1);

        var cmd = new ReorderCategoriesCommand(new List<CategoryReorderItem>());
        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.ReorderCategoriesAsync(_tenantId, _branch1, MenuId.New(), cmd, actor, CancellationToken.None));

        Assert.Contains("Actor is not authorized to manage catalog", ex.Message);
    }

    [Fact]
    public async Task BranchManager_OtherBranchMutation_ThrowsInvalidAuthorizationScopeException()
    {
        var service = CreateService();
        // Manager scoped to branch 1
        var manager = CreateActor(AuthRole.BranchManager, _tenantId, _branch1);

        // Attempting to mutate in branch 2
        var cmd = new CreateMenuCommand("B2 Menu", "b2-menu");
        await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.CreateMenuAsync(_tenantId, _branch2, cmd, manager, CancellationToken.None));
    }

    [Fact]
    public async Task RestaurantAdmin_CanPassPermissionCheck_InAnyBranchWithinTenant()
    {
        var service = CreateService();
        var admin = CreateActor(AuthRole.RestaurantAdmin, _tenantId);

        var cmd1 = new CreateMenuCommand("B1 Menu", "b1-menu");
        // Access check succeeds before DB call
        var ex = await Record.ExceptionAsync(() => service.CreateMenuAsync(_tenantId, _branch1, cmd1, admin, CancellationToken.None));
        Assert.False(ex is InvalidAuthorizationScopeException);
    }

    [Fact]
    public async Task BranchManager_CanPassPermissionCheck_InOwnBranch()
    {
        var service = CreateService();
        var manager = CreateActor(AuthRole.BranchManager, _tenantId, _branch1);

        var cmd = new CreateMenuCommand("Own Menu", "own-menu");
        // Access check succeeds before DB call
        var ex = await Record.ExceptionAsync(() => service.CreateMenuAsync(_tenantId, _branch1, cmd, manager, CancellationToken.None));
        Assert.False(ex is InvalidAuthorizationScopeException);
    }

    [Fact]
    public async Task SuperAdmin_PlatformScope_CannotCreateMenu_ThrowsInvalidAuthorizationScopeException()
    {
        var service = CreateService();
        var superAdmin = CreateActor(AuthRole.SuperAdmin, tenantId: null);

        var cmd = new CreateMenuCommand("SA Menu", "sa-menu");
        await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            service.CreateMenuAsync(_tenantId, _branch1, cmd, superAdmin, CancellationToken.None));
    }
}
