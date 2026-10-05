using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Common;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class ModifierEndpointsUnitTests
{
    private readonly IPermissionRegistry _registry = new PermissionRegistry();

    [Fact]
    public void ModifierGroupResult_SetsETagHeaderAndReturnsOkOrCreated()
    {
        var httpContext = new DefaultHttpContext();
        var token = Guid.NewGuid();
        var group = new ModifierGroupDto(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            Name: "Dressings",
            MinSelections: 0,
            MaxSelections: 1,
            SortOrder: 0,
            IsActive: true,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: null,
            ConcurrencyToken: token);

        var okResult = CatalogEndpoints.ModifierGroupResult(httpContext, group, StatusCodes.Status200OK);
        Assert.IsType<Ok<ModifierGroupDto>>(okResult);
        Assert.Equal($"\"{token:D}\"", httpContext.Response.Headers.ETag.ToString());

        var createdContext = new DefaultHttpContext();
        var createdResult = CatalogEndpoints.ModifierGroupResult(createdContext, group, StatusCodes.Status201Created);
        Assert.IsType<Created<ModifierGroupDto>>(createdResult);
        Assert.Equal($"\"{token:D}\"", createdContext.Response.Headers.ETag.ToString());
    }

    [Fact]
    public void ModifierOptionResult_SetsETagHeaderAndReturnsOkOrCreated()
    {
        var httpContext = new DefaultHttpContext();
        var token = Guid.NewGuid();
        var option = new ModifierOptionDto(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            ModifierGroupId: Guid.NewGuid(),
            Name: "Ranch",
            PriceDeltaMinorUnits: 50,
            SortOrder: 0,
            IsDefault: false,
            IsActive: true,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: null,
            ConcurrencyToken: token);

        var okResult = CatalogEndpoints.ModifierOptionResult(httpContext, option, StatusCodes.Status200OK);
        Assert.IsType<Ok<ModifierOptionDto>>(okResult);
        Assert.Equal($"\"{token:D}\"", httpContext.Response.Headers.ETag.ToString());

        var createdContext = new DefaultHttpContext();
        var createdResult = CatalogEndpoints.ModifierOptionResult(createdContext, option, StatusCodes.Status201Created);
        Assert.IsType<Created<ModifierOptionDto>>(createdResult);
        Assert.Equal($"\"{token:D}\"", createdContext.Response.Headers.ETag.ToString());
    }

    [Fact]
    public void ExtractConcurrencyToken_ExtractsFromBodyOrIfMatchHeader()
    {
        var expectedGuid = Guid.NewGuid();

        // 1. From body
        var bodyToken = CatalogEndpoints.ExtractConcurrencyToken(expectedGuid, new DefaultHttpContext().Request);
        Assert.Equal(expectedGuid, bodyToken);

        // 2. From If-Match header with quotes
        var headerContext = new DefaultHttpContext();
        headerContext.Request.Headers.IfMatch = $"\"{expectedGuid:D}\"";
        var headerToken = CatalogEndpoints.ExtractConcurrencyToken(null, headerContext.Request);
        Assert.Equal(expectedGuid, headerToken);

        // 3. Missing returns null
        var emptyContext = new DefaultHttpContext();
        var nullToken = CatalogEndpoints.ExtractConcurrencyToken(null, emptyContext.Request);
        Assert.Null(nullToken);
    }

    [Fact]
    public void RolePermissions_CatalogManage_GrantedToBranchManagerAndRestaurantAdmin()
    {
        // SuperAdmin cannot manage catalog (cannot bypass tenant boundaries)
        Assert.False(_registry.HasFullGrant(AuthRole.SuperAdmin, Permissions.MenuCatalogManage));
        Assert.True(_registry.HasFullGrant(AuthRole.RestaurantAdmin, Permissions.MenuCatalogManage));
        Assert.True(_registry.HasFullGrant(AuthRole.BranchManager, Permissions.MenuCatalogManage));

        Assert.False(_registry.HasFullGrant(AuthRole.Waiter, Permissions.MenuCatalogManage));
        Assert.False(_registry.HasFullGrant(AuthRole.Customer, Permissions.MenuCatalogManage));
        Assert.False(_registry.HasFullGrant(AuthRole.Kitchen, Permissions.MenuCatalogManage));
        Assert.False(_registry.HasFullGrant(AuthRole.Bar, Permissions.MenuCatalogManage));
    }

    [Fact]
    public void RolePermissions_PricingManage_GrantedToBranchManagerAndRestaurantAdmin()
    {
        Assert.False(_registry.HasFullGrant(AuthRole.SuperAdmin, Permissions.MenuPricingManage));
        Assert.True(_registry.HasFullGrant(AuthRole.RestaurantAdmin, Permissions.MenuPricingManage));
        Assert.True(_registry.HasFullGrant(AuthRole.BranchManager, Permissions.MenuPricingManage));

        Assert.False(_registry.HasFullGrant(AuthRole.Waiter, Permissions.MenuPricingManage));
        Assert.False(_registry.HasFullGrant(AuthRole.Customer, Permissions.MenuPricingManage));
    }

    [Fact]
    public void HandleException_DomainException_ReturnsBadRequestProblemDetails()
    {
        var ex = new DomainException("Test domain constraint violation.");
        var context = new DefaultHttpContext();

        var result = CatalogEndpoints.HandleException(ex, context);
        var problem = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public void HandleException_ConcurrencyConflict_ReturnsConflictProblemDetails()
    {
        var ex = new ConcurrencyConflictException("Stale token conflict.");
        var context = new DefaultHttpContext();

        var result = CatalogEndpoints.HandleException(ex, context);
        var problem = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task ReorderModifierOptionsHandler_MissingOrEmptyOptions_ReturnsBadRequest()
    {
        var mockService = new Mock<ICatalogService>();
        var tenantContext = new TenantContext(Guid.NewGuid(), isAuthenticated: true);
        var mockParser = new Mock<IJwtClaimPrincipalParser>();
        var context = new DefaultHttpContext();

        var emptyCmd = new ReorderModifierOptionsCommand(new List<ModifierOptionReorderItem>());
        var result = await CatalogEndpoints.ReorderModifierOptionsHandler(
            Guid.NewGuid(), Guid.NewGuid(), emptyCmd, context, mockService.Object, tenantContext, mockParser.Object, CancellationToken.None);

        var prob = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, prob.StatusCode);
    }

    [Fact]
    public async Task ReorderModifierOptionsHandler_EmptyOptionToken_ReturnsPreconditionFailed()
    {
        var mockService = new Mock<ICatalogService>();
        var tenantContext = new TenantContext(Guid.NewGuid(), isAuthenticated: true);
        var mockParser = new Mock<IJwtClaimPrincipalParser>();
        var context = new DefaultHttpContext();

        var cmd = new ReorderModifierOptionsCommand(new List<ModifierOptionReorderItem>
        {
            new(Guid.NewGuid(), 0, Guid.Empty)
        });
        var result = await CatalogEndpoints.ReorderModifierOptionsHandler(
            Guid.NewGuid(), Guid.NewGuid(), cmd, context, mockService.Object, tenantContext, mockParser.Object, CancellationToken.None);

        var prob = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, prob.StatusCode);
    }
}
