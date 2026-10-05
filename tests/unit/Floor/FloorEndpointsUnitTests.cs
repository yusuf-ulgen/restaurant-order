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
            ConcurrencyToken: token,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: null);

        var result = FloorEndpointHelpers.TableResult(httpContext, table, StatusCodes.Status201Created);
        var createdResult = Assert.IsAssignableFrom<Created<RestaurantTableDto>>(result);

        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.Equal($"\"{token:D}\"", httpContext.Response.Headers.ETag.ToString());
        Assert.Equal($"/api/v1/floor/branches/{table.BranchId}/tables/{table.Id}", createdResult.Location);
    }

    private static ProblemHttpResult AssertProblem(IResult result, int expectedStatusCode)
    {
        var problem = Assert.IsAssignableFrom<ProblemHttpResult>(result);
        Assert.Equal(expectedStatusCode, problem.StatusCode);
        return problem;
    }
}
