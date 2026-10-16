using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Api.Floor;

/// <summary>
/// Endpoints for customer table service requests (e.g., call waiter, water, napkin, assistance).
/// Enforces customer-role scope, own-session isolation, double-submit CSRF, idempotency keys, and rate limits.
/// </summary>
public static class CustomerServiceRequestEndpoints
{
    public static IEndpointRouteBuilder MapCustomerServiceRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customer/service-requests")
            .WithTags("Customer Service Requests")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "Customer" });

        // POST /api/v1/customer/service-requests
        group.MapPost("", async (
            [FromBody] CreateCustomerServiceRequest request,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ICustomerServiceRequestService service,
            CancellationToken ct) =>
        {
            try
            {
                var actor = FloorEndpointHelpers.GetActor(context, parser);
                if (actor.Role != AuthRole.Customer)
                {
                    return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", "Only customers may submit service requests.");
                }

                if (!actor.Scope.TenantId.HasValue ||
                    !actor.Scope.BranchId.HasValue ||
                    !actor.Scope.TableSessionId.HasValue)
                {
                    return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Customer token is missing required session claims.");
                }

                // Enforce mandatory Idempotency-Key HTTP header
                if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var rawKey) ||
                    string.IsNullOrWhiteSpace(rawKey))
                {
                    return ProblemResult(context, StatusCodes.Status400BadRequest, "Bad Request", "Idempotency-Key header is required.");
                }

                var idempotencyKey = rawKey.ToString().Trim();
                var tenantId = new TenantId(actor.Scope.TenantId.Value);
                var branchId = new BranchId(actor.Scope.BranchId.Value);
                var sessionId = new DiningSessionId(actor.Scope.TableSessionId.Value);

                var result = await service.CreateRequestAsync(
                    tenantId,
                    branchId,
                    sessionId,
                    request,
                    idempotencyKey,
                    actor,
                    ct);

                context.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
                return Results.Ok(result);
            }
            catch (ServiceRequestRateLimitException rlex)
            {
                context.Response.Headers["Retry-After"] = rlex.RetryAfterSeconds.ToString();
                return Results.Json(new
                {
                    type = "https://httpstatuses.com/429",
                    title = "Too Many Requests",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = rlex.Message,
                    retryAfterSeconds = rlex.RetryAfterSeconds,
                    instance = context.Request.Path.Value
                }, statusCode: StatusCodes.Status429TooManyRequests, contentType: "application/problem+json");
            }
            catch (ConcurrencyConflictException cex)
            {
                return ProblemResult(context, StatusCodes.Status409Conflict, "Conflict", cex.Message);
            }
            catch (ResourceNotFoundException nfex)
            {
                return ProblemResult(context, StatusCodes.Status404NotFound, "Not Found", nfex.Message);
            }
            catch (InvalidAuthorizationScopeException scpEx)
            {
                return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", scpEx.Message);
            }
            catch (DomainException dex)
            {
                return ProblemResult(context, StatusCodes.Status400BadRequest, "Bad Request", dex.Message);
            }
            catch (ArgumentException aex)
            {
                return ProblemResult(context, StatusCodes.Status400BadRequest, "Bad Request", aex.Message);
            }
            catch (JwtClaimValidationException jex)
            {
                return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", $"Invalid session claims: {jex.Message}");
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("RestaurantOrder.Api.Floor.CustomerServiceRequests");
                logger?.LogError(ex, "Unexpected error creating customer service request: {Message}", ex.Message);
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("CreateCustomerServiceRequest")
        .WithSummary("Submit a new table service request (e.g. Call Waiter, Water, Napkin, Assistance)");

        // GET /api/v1/customer/service-requests/open
        group.MapGet("/open", async (
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ICustomerServiceRequestService service,
            CancellationToken ct) =>
        {
            try
            {
                var actor = FloorEndpointHelpers.GetActor(context, parser);
                if (actor.Role != AuthRole.Customer)
                {
                    return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", "Only customers may view session service requests.");
                }

                if (!actor.Scope.TenantId.HasValue ||
                    !actor.Scope.BranchId.HasValue ||
                    !actor.Scope.TableSessionId.HasValue)
                {
                    return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Customer token is missing required session claims.");
                }

                var tenantId = new TenantId(actor.Scope.TenantId.Value);
                var branchId = new BranchId(actor.Scope.BranchId.Value);
                var sessionId = new DiningSessionId(actor.Scope.TableSessionId.Value);

                var list = await service.GetOpenRequestsAsync(
                    tenantId,
                    branchId,
                    sessionId,
                    actor,
                    ct);

                return Results.Ok(list);
            }
            catch (InvalidAuthorizationScopeException scpEx)
            {
                return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", scpEx.Message);
            }
            catch (JwtClaimValidationException jex)
            {
                return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", $"Invalid session claims: {jex.Message}");
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("RestaurantOrder.Api.Floor.CustomerServiceRequests");
                logger?.LogError(ex, "Unexpected error retrieving open service requests: {Message}", ex.Message);
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("GetOpenCustomerServiceRequests")
        .WithSummary("Retrieve active open service requests for the current customer session");

        // DELETE /api/v1/customer/service-requests/{requestId:guid}
        group.MapDelete("/{requestId:guid}", async (
            Guid requestId,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ICustomerServiceRequestService service,
            CancellationToken ct) =>
        {
            try
            {
                var actor = FloorEndpointHelpers.GetActor(context, parser);
                if (actor.Role != AuthRole.Customer)
                {
                    return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", "Only customers may cancel session service requests.");
                }

                if (!actor.Scope.TenantId.HasValue ||
                    !actor.Scope.BranchId.HasValue ||
                    !actor.Scope.TableSessionId.HasValue)
                {
                    return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Customer token is missing required session claims.");
                }

                var tenantId = new TenantId(actor.Scope.TenantId.Value);
                var branchId = new BranchId(actor.Scope.BranchId.Value);
                var sessionId = new DiningSessionId(actor.Scope.TableSessionId.Value);

                var result = await service.CancelRequestAsync(
                    tenantId,
                    branchId,
                    sessionId,
                    new ServiceRequestId(requestId),
                    actor,
                    ct);

                return Results.Ok(result);
            }
            catch (ResourceNotFoundException nfex)
            {
                return ProblemResult(context, StatusCodes.Status404NotFound, "Not Found", nfex.Message);
            }
            catch (InvalidAuthorizationScopeException scpEx)
            {
                return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", scpEx.Message);
            }
            catch (DomainException dex)
            {
                return ProblemResult(context, StatusCodes.Status400BadRequest, "Bad Request", dex.Message);
            }
            catch (JwtClaimValidationException jex)
            {
                return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", $"Invalid session claims: {jex.Message}");
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("RestaurantOrder.Api.Floor.CustomerServiceRequests");
                logger?.LogError(ex, "Unexpected error cancelling service request: {Message}", ex.Message);
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("CancelCustomerServiceRequest")
        .WithSummary("Cancel an open service request initiated by the current customer session");

        return app;
    }

    private static IResult ProblemResult(HttpContext context, int status, string title, string detail)
    {
        return Results.Json(new
        {
            type = $"https://httpstatuses.com/{status}",
            title,
            status,
            detail,
            instance = context.Request.Path.Value
        }, statusCode: status, contentType: "application/problem+json");
    }
}
