using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Common;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class CatalogEndpointsUnitTests
{
    private readonly IPermissionRegistry _registry = new PermissionRegistry();

    [Fact]
    public void HandleException_MapsAllExpectedDomainAndConcurrencyExceptions()
    {
        var notFound = CatalogEndpoints.HandleException(new ResourceNotFoundException("Menu not found"));
        AssertProblem(notFound, StatusCodes.Status404NotFound);

        var dupSlug = CatalogEndpoints.HandleException(new DuplicateSlugException("Slug exists"));
        AssertProblem(dupSlug, StatusCodes.Status409Conflict);

        var concConflict = CatalogEndpoints.HandleException(new ConcurrencyConflictException("Stale token"));
        AssertProblem(concConflict, StatusCodes.Status409Conflict);

        var concPrecond = CatalogEndpoints.HandleException(new ConcurrencyPreconditionException("Missing token"));
        AssertProblem(concPrecond, StatusCodes.Status412PreconditionFailed);

        var invalidScope = CatalogEndpoints.HandleException(new InvalidAuthorizationScopeException("Forbidden branch"));
        AssertProblem(invalidScope, StatusCodes.Status403Forbidden);

        var domainEx = CatalogEndpoints.HandleException(new DomainException("Menu is archived"));
        AssertProblem(domainEx, StatusCodes.Status400BadRequest);

        var argEx = CatalogEndpoints.HandleException(new ArgumentException("Invalid argument"));
        AssertProblem(argEx, StatusCodes.Status400BadRequest);

        var genericEx = CatalogEndpoints.HandleException(new InvalidOperationException("Internal db failure"));
        var genericProblem = AssertProblem(genericEx, StatusCodes.Status500InternalServerError);
        Assert.Equal("An unexpected error occurred while processing your request.", genericProblem.ProblemDetails.Detail);
        Assert.NotNull(genericProblem.ProblemDetails.Extensions["correlationId"]);
    }

    [Fact]
    public void HandleException_WithHttpContext_PreservesCorrelationIdHeaderAndMasksSensitiveDetail()
    {
        var httpContext = new DefaultHttpContext();
        var correlationId = Guid.NewGuid().ToString("D");
        httpContext.Request.Headers["X-Correlation-Id"] = correlationId;

        var result = CatalogEndpoints.HandleException(new InvalidOperationException("Secret db string"), httpContext);
        var problem = AssertProblem(result, StatusCodes.Status500InternalServerError);

        Assert.Equal("An unexpected error occurred while processing your request.", problem.ProblemDetails.Detail);
        Assert.DoesNotContain("Secret db string", problem.ProblemDetails.Detail);
        Assert.Equal(correlationId, problem.ProblemDetails.Extensions["correlationId"]?.ToString());
        Assert.Equal(correlationId, httpContext.Response.Headers["X-Correlation-Id"].ToString());
    }

    [Fact]
    public void ExtractConcurrencyToken_ExtractsFromBodyOrIfMatchHeader()
    {
        var bodyToken = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();

        // 1. From body token
        var result1 = CatalogEndpoints.ExtractConcurrencyToken(bodyToken, httpContext.Request);
        Assert.Equal(bodyToken, result1);

        // 2. From valid quoted If-Match header
        var headerToken = Guid.NewGuid();
        httpContext.Request.Headers.IfMatch = $"\"{headerToken:D}\"";
        var result2 = CatalogEndpoints.ExtractConcurrencyToken(null, httpContext.Request);
        Assert.Equal(headerToken, result2);

        // 3. Null when absent
        var emptyContext = new DefaultHttpContext();
        var result3 = CatalogEndpoints.ExtractConcurrencyToken(null, emptyContext.Request);
        Assert.Null(result3);
    }

    [Fact]
    public void MenuResult_SetsETagHeaderAndReturnsExpectedStatusCode()
    {
        var httpContext = new DefaultHttpContext();
        var token = Guid.NewGuid();
        var menu = new MenuDto(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            Name: "Test Menu",
            Slug: "test-menu",
            Description: null,
            Status: "Draft",
            SortOrder: 0,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: null,
            ConcurrencyToken: token);

        var okResult = CatalogEndpoints.MenuResult(httpContext, menu, StatusCodes.Status200OK);
        Assert.IsType<Ok<MenuDto>>(okResult);
        Assert.Equal($"\"{token:D}\"", httpContext.Response.Headers.ETag.ToString());

        var createdContext = new DefaultHttpContext();
        var createdResult = CatalogEndpoints.MenuResult(createdContext, menu, StatusCodes.Status201Created);
        Assert.IsType<Created<MenuDto>>(createdResult);
        Assert.Equal($"\"{token:D}\"", createdContext.Response.Headers.ETag.ToString());
    }

    [Fact]
    public void CategoryResult_SetsETagHeaderAndReturnsExpectedStatusCode()
    {
        var httpContext = new DefaultHttpContext();
        var token = Guid.NewGuid();
        var category = new MenuCategoryDto(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            MenuId: Guid.NewGuid(),
            Name: "Starters",
            Slug: "starters",
            Description: null,
            SortOrder: 0,
            IsActive: true,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: null,
            ConcurrencyToken: token);

        var okResult = CatalogEndpoints.CategoryResult(httpContext, category, StatusCodes.Status200OK);
        Assert.IsType<Ok<MenuCategoryDto>>(okResult);
        Assert.Equal($"\"{token:D}\"", httpContext.Response.Headers.ETag.ToString());

        var createdContext = new DefaultHttpContext();
        var createdResult = CatalogEndpoints.CategoryResult(createdContext, category, StatusCodes.Status201Created);
        Assert.IsType<Created<MenuCategoryDto>>(createdResult);
        Assert.Equal($"\"{token:D}\"", createdContext.Response.Headers.ETag.ToString());
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, PermissionGrantType.Full, PermissionGrantType.Denied)]
    [InlineData(AuthRole.RestaurantAdmin, PermissionGrantType.Full, PermissionGrantType.Full)]
    [InlineData(AuthRole.BranchManager, PermissionGrantType.Full, PermissionGrantType.Full)]
    [InlineData(AuthRole.Cashier, PermissionGrantType.Full, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Kitchen, PermissionGrantType.Full, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Bar, PermissionGrantType.Full, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Waiter, PermissionGrantType.Full, PermissionGrantType.Denied)]
    [InlineData(AuthRole.Customer, PermissionGrantType.Full, PermissionGrantType.Denied)]
    public void CatalogPermissions_RbacMatrix_AdheresToSpecifications(
        AuthRole role,
        PermissionGrantType expectedViewGrant,
        PermissionGrantType expectedManageGrant)
    {
        var viewGrant = _registry.GetGrantType(role, Permissions.MenuCatalogView);
        var manageGrant = _registry.GetGrantType(role, Permissions.MenuCatalogManage);

        Assert.Equal(expectedViewGrant, viewGrant);
        Assert.Equal(expectedManageGrant, manageGrant);
    }

    private static ProblemHttpResult AssertProblem(IResult result, int expectedStatusCode)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(expectedStatusCode, problem.StatusCode);
        Assert.NotNull(problem.ProblemDetails);
        return problem;
    }
}
