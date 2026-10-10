using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Common;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class FloorEndpointsUnitTests
{
    [Fact]
    public void HandleException_MapsAllExpectedDomainAndConcurrencyExceptions()
    {
        var notFound = FloorEndpointHelpers.HandleException(new ResourceNotFoundException("Table not found"));
        AssertProblem(notFound, StatusCodes.Status404NotFound);

        var dupCode = FloorEndpointHelpers.HandleException(new DuplicateCodeException("Table number exists"));
        AssertProblem(dupCode, StatusCodes.Status409Conflict);

        var concConflict = FloorEndpointHelpers.HandleException(new ConcurrencyConflictException("Stale token"));
        AssertProblem(concConflict, StatusCodes.Status409Conflict);

        var concPrecond = FloorEndpointHelpers.HandleException(new ConcurrencyPreconditionException("Missing token"));
        AssertProblem(concPrecond, StatusCodes.Status412PreconditionFailed);

        var invalidScope = FloorEndpointHelpers.HandleException(new InvalidAuthorizationScopeException("Forbidden branch"));
        AssertProblem(invalidScope, StatusCodes.Status403Forbidden);

        var domainEx = FloorEndpointHelpers.HandleException(new DomainException("Invalid capacity"));
        AssertProblem(domainEx, StatusCodes.Status400BadRequest);

        var argEx = FloorEndpointHelpers.HandleException(new ArgumentException("Route branch mismatch"));
        AssertProblem(argEx, StatusCodes.Status400BadRequest);

        var genericEx = FloorEndpointHelpers.HandleException(new InvalidOperationException("Internal db failure"));
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

        var result = FloorEndpointHelpers.HandleException(new InvalidOperationException("Secret db string"), httpContext);
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
        var result1 = FloorEndpointHelpers.ExtractConcurrencyToken(bodyToken, httpContext.Request);
        Assert.Equal(bodyToken, result1);

        // 2. From valid quoted If-Match header
        var headerToken = Guid.NewGuid();
        httpContext.Request.Headers.IfMatch = $"\"{headerToken:D}\"";
        var result2 = FloorEndpointHelpers.ExtractConcurrencyToken(null, httpContext.Request);
        Assert.Equal(headerToken, result2);

        // 3. Null when absent
        var emptyContext = new DefaultHttpContext();
        var result3 = FloorEndpointHelpers.ExtractConcurrencyToken(null, emptyContext.Request);
        Assert.Null(result3);
    }

    [Fact]
    public void TableResult_SetsETagHeaderAndReturnsExpectedStatusCode()
    {
        var httpContext = new DefaultHttpContext();
        var token = Guid.NewGuid();
        var table = new RestaurantTableDto(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            DiningAreaId: Guid.NewGuid(),
            TableNumber: "T-01",
            Name: "Table 1",
            Capacity: 4,
            PositionX: 100,
            PositionY: 200,
            Width: 100,
            Height: 100,
            RotationDegrees: 0,
            Shape: "Square",
            IsActive: true,
            QrVersion: 1,
            PublicCode: "tbl-pub-code-01",
            ConcurrencyToken: token,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: null);

        var result = FloorEndpointHelpers.TableResult(httpContext, table, StatusCodes.Status201Created);
        var createdResult = Assert.IsAssignableFrom<Created<RestaurantTableDto>>(result);

        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.Equal($"\"{token:D}\"", httpContext.Response.Headers.ETag.ToString());
        Assert.Equal($"/api/v1/floor/branches/{table.BranchId}/tables/{table.Id}", createdResult.Location);

        var okResult = FloorEndpointHelpers.TableResult(httpContext, table, StatusCodes.Status200OK);
        Assert.IsAssignableFrom<Ok<RestaurantTableDto>>(okResult);
    }

    [Fact]
    public void SessionResult_SetsETagHeaderAndReturnsExpectedStatusCode()
    {
        var httpContext = new DefaultHttpContext();
        var token = Guid.NewGuid();
        var session = new DiningSessionDto(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            TableId: Guid.NewGuid(),
            Status: "Open",
            GuestCount: 2,
            AssignedWaiterId: null,
            OpenedAtUtc: DateTimeOffset.UtcNow,
            ActivatedAtUtc: null,
            BillRequestedAtUtc: null,
            ClosedAtUtc: null,
            CloseReason: null,
            MergedIntoSessionId: null,
            ConcurrencyToken: token,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: null);

        var createdResult = FloorEndpointHelpers.SessionResult(httpContext, session, StatusCodes.Status201Created);
        var created = Assert.IsAssignableFrom<Created<DiningSessionDto>>(createdResult);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal($"\"{token:D}\"", httpContext.Response.Headers.ETag.ToString());
        Assert.Equal($"/api/v1/floor/branches/{session.BranchId}/sessions/{session.Id}", created.Location);

        var okResult = FloorEndpointHelpers.SessionResult(httpContext, session, StatusCodes.Status200OK);
        Assert.IsAssignableFrom<Ok<DiningSessionDto>>(okResult);
    }

    [Fact]
    public void ExtractConcurrencyToken_HandlesEmptyGuidAndMalformedHeader()
    {
        var httpContext = new DefaultHttpContext();

        // Empty body token should fallback
        httpContext.Request.Headers.IfMatch = "not-a-valid-guid";
        var result = FloorEndpointHelpers.ExtractConcurrencyToken(Guid.Empty, httpContext.Request);
        Assert.Null(result);

        // Whitespace header
        httpContext.Request.Headers.IfMatch = "   ";
        var result2 = FloorEndpointHelpers.ExtractConcurrencyToken(null, httpContext.Request);
        Assert.Null(result2);
    }

    [Fact]
    public void ResolveCorrelationId_FallbackBranches()
    {
        // 1. Response header present
        var ctx1 = new DefaultHttpContext();
        ctx1.Response.Headers["X-Correlation-Id"] = "resp-123";
        Assert.Equal("resp-123", FloorEndpointHelpers.ResolveCorrelationId(ctx1));

        // 2. TraceIdentifier present
        var ctx2 = new DefaultHttpContext();
        ctx2.TraceIdentifier = "trace-456";
        Assert.Equal("trace-456", FloorEndpointHelpers.ResolveCorrelationId(ctx2));

        // 3. Null context
        var corr = FloorEndpointHelpers.ResolveCorrelationId(null);
        Assert.True(Guid.TryParse(corr, out _));
    }

    [Fact]
    public void GetActor_ParsesPrincipalFromHttpContextClaims()
    {
        var httpContext = new DefaultHttpContext();
        var claims = new[]
        {
            new System.Security.Claims.Claim("sub", "user-123"),
            new System.Security.Claims.Claim("email", "staff@test.com"),
            new System.Security.Claims.Claim("role", "BranchManager")
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);

        var parserMock = new Moq.Mock<RestaurantOrder.Application.Auth.IJwtClaimPrincipalParser>();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var expectedPrincipal = new AuthenticatedPrincipal(
            Guid.NewGuid(),
            PrincipalType.Staff,
            AuthRole.BranchManager,
            AuthorizationScope.ForBranch(new RestaurantOrder.Domain.Tenants.TenantId(tenantId), new RestaurantOrder.Domain.Branches.BranchId(branchId)),
            Guid.NewGuid(),
            AuthenticationMethod.Password,
            1);

        parserMock.Setup(p => p.ParsePrincipal(Moq.It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(expectedPrincipal);

        var actor = FloorEndpointHelpers.GetActor(httpContext, parserMock.Object);

        Assert.NotNull(actor);
        Assert.Equal(expectedPrincipal.SubjectId, actor.SubjectId);
        Assert.Equal(AuthRole.BranchManager, actor.Role);
    }

    [Fact]
    public void ResolveTenantId_ResolvesFromTenantContextOrActorScopeOrThrows()
    {
        var tenantId = Guid.NewGuid();
        var actor = new AuthenticatedPrincipal(
            Guid.NewGuid(),
            PrincipalType.Staff,
            AuthRole.RestaurantAdmin,
            AuthorizationScope.ForTenant(new RestaurantOrder.Domain.Tenants.TenantId(tenantId)),
            Guid.NewGuid(),
            AuthenticationMethod.Password,
            1);

        // 1. From ITenantContext
        var mockContext = new Moq.Mock<RestaurantOrder.Application.Tenancy.ITenantContext>();
        mockContext.Setup(c => c.HasTenant).Returns(true);
        mockContext.Setup(c => c.TenantId).Returns(tenantId);

        var res1 = FloorEndpointHelpers.ResolveTenantId(mockContext.Object, actor);
        Assert.Equal(tenantId, res1.Value);

        // 2. Fallback to actor scope when tenant context empty
        var emptyContext = new Moq.Mock<RestaurantOrder.Application.Tenancy.ITenantContext>();
        emptyContext.Setup(c => c.HasTenant).Returns(false);
        emptyContext.Setup(c => c.TenantId).Returns((Guid?)null);

        var res2 = FloorEndpointHelpers.ResolveTenantId(emptyContext.Object, actor);
        Assert.Equal(tenantId, res2.Value);

        // 3. Throws when both are missing
        var noScopeActor = new AuthenticatedPrincipal(
            Guid.NewGuid(),
            PrincipalType.Staff,
            AuthRole.SuperAdmin,
            AuthorizationScope.Platform(),
            Guid.NewGuid(),
            AuthenticationMethod.Password,
            1);

        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            FloorEndpointHelpers.ResolveTenantId(emptyContext.Object, noScopeActor));
    }

    [Fact]
    public void HandleException_WithLoggerFactory_LogsHandledAndUnhandledExceptions()
    {
        var serviceProvider = new Moq.Mock<IServiceProvider>();
        var loggerFactory = new Moq.Mock<Microsoft.Extensions.Logging.ILoggerFactory>();
        var logger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger>();
        loggerFactory.Setup(f => f.CreateLogger(Moq.It.IsAny<string>())).Returns(logger.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory))).Returns(loggerFactory.Object);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider.Object };

        var notFoundResult = FloorEndpointHelpers.HandleException(new ResourceNotFoundException("Not found test"), httpContext);
        AssertProblem(notFoundResult, StatusCodes.Status404NotFound);

        var unhandledResult = FloorEndpointHelpers.HandleException(new InvalidOperationException("Fatal error test"), httpContext);
        AssertProblem(unhandledResult, StatusCodes.Status500InternalServerError);
    }

    private static ProblemHttpResult AssertProblem(IResult result, int expectedStatusCode)
    {
        var problem = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal(expectedStatusCode, problem.StatusCode);
        return problem;
    }
}
