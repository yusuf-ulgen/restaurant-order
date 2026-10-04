using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.FeatureFlags;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.RestaurantConfig;

public class RestaurantConfigEndpointsUnitTests
{
    [Fact]
    public void HandleException_MapsAllExpectedDomainAndConcurrencyExceptions()
    {
        var notFound = RestaurantConfigEndpoints.HandleException(new ResourceNotFoundException("Not found test"));
        AssertProblem(notFound, StatusCodes.Status404NotFound);

        var dupCode = RestaurantConfigEndpoints.HandleException(new DuplicateCodeException("Code exists"));
        AssertProblem(dupCode, StatusCodes.Status409Conflict);

        var dupSlug = RestaurantConfigEndpoints.HandleException(new DuplicateSlugException("Slug exists"));
        AssertProblem(dupSlug, StatusCodes.Status409Conflict);

        var concConflict = RestaurantConfigEndpoints.HandleException(new ConcurrencyConflictException("Conflict"));
        AssertProblem(concConflict, StatusCodes.Status409Conflict);

        var concPrecond = RestaurantConfigEndpoints.HandleException(new ConcurrencyPreconditionException("Missing token"));
        AssertProblem(concPrecond, StatusCodes.Status412PreconditionFailed);

        var invalidScope = RestaurantConfigEndpoints.HandleException(new InvalidAuthorizationScopeException("Forbidden"));
        AssertProblem(invalidScope, StatusCodes.Status403Forbidden);

        var domainEx = RestaurantConfigEndpoints.HandleException(new DomainException("Invalid domain logic"));
        AssertProblem(domainEx, StatusCodes.Status400BadRequest);

        var argEx = RestaurantConfigEndpoints.HandleException(new ArgumentException("Bad argument"));
        AssertProblem(argEx, StatusCodes.Status400BadRequest);

        var genericEx = RestaurantConfigEndpoints.HandleException(new InvalidOperationException("Generic failure"));
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

        var result = RestaurantConfigEndpoints.HandleException(new InvalidOperationException("Database password was leaked"), httpContext);
        var problem = AssertProblem(result, StatusCodes.Status500InternalServerError);

        Assert.Equal("An unexpected error occurred while processing your request.", problem.ProblemDetails.Detail);
        Assert.DoesNotContain("Database password", problem.ProblemDetails.Detail);
        Assert.Equal(correlationId, problem.ProblemDetails.Extensions["correlationId"]?.ToString());
        Assert.Equal(correlationId, httpContext.Response.Headers["X-Correlation-Id"].ToString());
    }

    [Fact]
    public void ExtractConcurrencyToken_ExtractsFromBodyOrIfMatchHeader()
    {
        var bodyToken = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();

        // 1. From body token
        var result1 = RestaurantConfigEndpoints.ExtractConcurrencyToken(bodyToken, httpContext.Request);
        Assert.Equal(bodyToken, result1);

        // 2. From valid quoted If-Match header
        var headerToken = Guid.NewGuid();
        httpContext.Request.Headers.IfMatch = $"\"{headerToken:D}\"";
        var result2 = RestaurantConfigEndpoints.ExtractConcurrencyToken(null, httpContext.Request);
        Assert.Equal(headerToken, result2);

        // 3. From unquoted If-Match header
        httpContext.Request.Headers.IfMatch = headerToken.ToString("D");
        var result3 = RestaurantConfigEndpoints.ExtractConcurrencyToken(null, httpContext.Request);
        Assert.Equal(headerToken, result3);

        // 4. Invalid header
        httpContext.Request.Headers.IfMatch = "invalid-guid";
        var result4 = RestaurantConfigEndpoints.ExtractConcurrencyToken(null, httpContext.Request);
        Assert.Null(result4);

        // 5. Empty header and null body
        httpContext.Request.Headers.IfMatch = string.Empty;
        var result5 = RestaurantConfigEndpoints.ExtractConcurrencyToken(null, httpContext.Request);
        Assert.Null(result5);
    }

    [Fact]
    public void ResolveTenantId_ResolvesFromContextOrActorScope()
    {
        var ctxTenantId = Guid.NewGuid();
        var actorTenantId = Guid.NewGuid();

        var tenantContext = new TenantContext(ctxTenantId, isAuthenticated: true);
        var actor = CreateActor(AuthRole.RestaurantAdmin, actorTenantId);

        // 1. Context present
        var resolved1 = RestaurantConfigEndpoints.ResolveTenantId(tenantContext, actor);
        Assert.Equal(ctxTenantId, resolved1.Value);

        // 2. Context empty, actor scope present
        var resolved2 = RestaurantConfigEndpoints.ResolveTenantId(TenantContext.Empty, actor);
        Assert.Equal(actorTenantId, resolved2.Value);

        // 3. Both missing throws
        var actorNoScope = CreateActor(AuthRole.SuperAdmin, null);
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            RestaurantConfigEndpoints.ResolveTenantId(TenantContext.Empty, actorNoScope));
    }

    [Fact]
    public void BrandResult_SetsETagAndReturnsCorrectStatusCode()
    {
        var brand = new BrandDto(Guid.NewGuid(), Guid.NewGuid(), "Brand 1", "brand-1", "Active", DateTime.UtcNow, null, Guid.NewGuid());

        var ctx201 = new DefaultHttpContext();
        var result201 = RestaurantConfigEndpoints.BrandResult(ctx201, brand, StatusCodes.Status201Created);
        Assert.NotNull(result201);
        Assert.Equal($"\"{brand.ConcurrencyToken:D}\"", ctx201.Response.Headers.ETag);

        var ctx200 = new DefaultHttpContext();
        var result200 = RestaurantConfigEndpoints.BrandResult(ctx200, brand, StatusCodes.Status200OK);
        Assert.NotNull(result200);
        Assert.Equal($"\"{brand.ConcurrencyToken:D}\"", ctx200.Response.Headers.ETag);
    }

    [Fact]
    public void BranchResult_SetsETagAndReturnsCorrectStatusCode()
    {
        var branch = new BranchDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Branch 1", "branch-1", "Active", "Europe/Istanbul", "TRY", DateTime.UtcNow, null, Guid.NewGuid());

        var ctx201 = new DefaultHttpContext();
        var result201 = RestaurantConfigEndpoints.BranchResult(ctx201, branch, StatusCodes.Status201Created);
        Assert.NotNull(result201);
        Assert.Equal($"\"{branch.ConcurrencyToken:D}\"", ctx201.Response.Headers.ETag);

        var ctx200 = new DefaultHttpContext();
        var result200 = RestaurantConfigEndpoints.BranchResult(ctx200, branch, StatusCodes.Status200OK);
        Assert.NotNull(result200);
        Assert.Equal($"\"{branch.ConcurrencyToken:D}\"", ctx200.Response.Headers.ETag);
    }

    [Fact]
    public void ValidateFeatureFlagsPayload_ValidatesKeysProperly()
    {
        // 1. Null flags
        var nullRes = RestaurantConfigEndpoints.ValidateFeatureFlagsPayload(null);
        Assert.Null(nullRes);

        // 2. Valid flags
        var validFlags = new Dictionary<string, bool>
        {
            [FeatureFlagKey.CustomerQrOrdering] = true,
            [FeatureFlagKey.Tips] = false
        };
        var validRes = RestaurantConfigEndpoints.ValidateFeatureFlagsPayload(validFlags);
        Assert.Null(validRes);

        // 3. Invalid flag key returns 400 ProblemDetails
        var invalidFlags = new Dictionary<string, bool>
        {
            ["NonExistentFlagKey"] = true
        };
        var invalidRes = RestaurantConfigEndpoints.ValidateFeatureFlagsPayload(invalidFlags);
        Assert.NotNull(invalidRes);
        AssertProblem(invalidRes, StatusCodes.Status400BadRequest);
    }

    private static AuthenticatedPrincipal CreateActor(AuthRole role, Guid? tenantId)
    {
        var scope = tenantId.HasValue
            ? AuthorizationScope.ForTenant(new TenantId(tenantId.Value))
            : AuthorizationScope.Platform();

        return new AuthenticatedPrincipal(
            subjectId: Guid.NewGuid(),
            principalType: PrincipalType.Staff,
            role: role,
            scope: scope,
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);
    }

    private static ProblemHttpResult AssertProblem(IResult result, int expectedStatusCode)
    {
        var problemHttpResult = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal(expectedStatusCode, problemHttpResult.StatusCode);
        return problemHttpResult;
    }
}
