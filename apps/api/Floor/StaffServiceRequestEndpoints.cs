using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Tenancy;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;

namespace RestaurantOrder.Api.Floor;

/// <summary>
/// Staff endpoints for managing customer service requests (list, acknowledge, complete).
/// Enforces fail-closed multi-tenant RBAC (service.request.manage),
/// concurrency precondition checks via ConcurrencyToken / If-Match header,
/// and returns enriched StaffServiceRequestDto models.
/// </summary>
public static class StaffServiceRequestEndpoints
{
    public static IEndpointRouteBuilder MapStaffServiceRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var branchGroup = app.MapGroup("/api/v1/floor/branches/{branchId:guid}/service-requests")
            .WithTags("Staff Service Requests")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "RestaurantAdmin,BranchManager,Cashier,Waiter" });

        // GET /api/v1/floor/branches/{branchId}/service-requests
        branchGroup.MapGet("", async (
            Guid branchId,
            [FromQuery] string? status,
            [FromQuery] Guid? tableId,
            [FromQuery] Guid? sessionId,
            [FromQuery] int? limit,
            [FromQuery] int? offset,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ITenantContext tenantContext,
            IStaffServiceRequestService service,
            CancellationToken ct) =>
        {
            try
            {
                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);

                bool activeOnly = false;
                ServiceRequestStatus? statusFilter = null;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    if (string.Equals(status.Trim(), "active", StringComparison.OrdinalIgnoreCase))
                    {
                        activeOnly = true;
                    }
                    else if (Enum.TryParse<ServiceRequestStatus>(status, true, out var parsedStatus))
                    {
                        statusFilter = parsedStatus;
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid status filter '{status}'.");
                    }
                }

                var list = await service.ListServiceRequestsAsync(
                    tenantId,
                    new BranchId(branchId),
                    statusFilter,
                    tableId.HasValue ? new RestaurantTableId(tableId.Value) : null,
                    sessionId.HasValue ? new DiningSessionId(sessionId.Value) : null,
                    limit ?? 50,
                    offset ?? 0,
                    actor,
                    activeOnly,
                    ct);

                return Results.Ok(list);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("ListStaffServiceRequests")
        .WithSummary("List customer service requests for a branch");

        // POST /api/v1/floor/branches/{branchId}/service-requests/{requestId}/acknowledge
        branchGroup.MapPost("/{requestId:guid}/acknowledge", async (
            Guid branchId,
            Guid requestId,
            [FromBody] TransitionSessionRequest? request,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ITenantContext tenantContext,
            IStaffServiceRequestService service,
            CancellationToken ct) =>
        {
            try
            {
                var token = FloorEndpointHelpers.ExtractConcurrencyToken(request?.ConcurrencyToken, context.Request);
                if (!token.HasValue)
                {
                    throw new ConcurrencyPreconditionException("Concurrency token is required via If-Match header or body.");
                }

                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);

                var result = await service.AcknowledgeRequestAsync(
                    tenantId,
                    new BranchId(branchId),
                    new ServiceRequestId(requestId),
                    token.Value,
                    actor,
                    ct);

                context.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("AcknowledgeStaffServiceRequest")
        .WithSummary("Acknowledge an open service request by staff");

        // POST /api/v1/floor/branches/{branchId}/service-requests/{requestId}/complete
        branchGroup.MapPost("/{requestId:guid}/complete", async (
            Guid branchId,
            Guid requestId,
            [FromBody] TransitionSessionRequest? request,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ITenantContext tenantContext,
            IStaffServiceRequestService service,
            CancellationToken ct) =>
        {
            try
            {
                var token = FloorEndpointHelpers.ExtractConcurrencyToken(request?.ConcurrencyToken, context.Request);
                if (!token.HasValue)
                {
                    throw new ConcurrencyPreconditionException("Concurrency token is required via If-Match header or body.");
                }

                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);

                var result = await service.CompleteRequestAsync(
                    tenantId,
                    new BranchId(branchId),
                    new ServiceRequestId(requestId),
                    token.Value,
                    actor,
                    ct);

                context.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("CompleteStaffServiceRequest")
        .WithSummary("Complete a service request by staff");

        // Session scoped convenience endpoint: GET /api/v1/floor/branches/{branchId}/sessions/{sessionId}/service-requests
        var sessionGroup = app.MapGroup("/api/v1/floor/branches/{branchId:guid}/sessions/{sessionId:guid}/service-requests")
            .WithTags("Staff Service Requests")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "RestaurantAdmin,BranchManager,Cashier,Waiter" });

        sessionGroup.MapGet("", async (
            Guid branchId,
            Guid sessionId,
            [FromQuery] string? status,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ITenantContext tenantContext,
            IStaffServiceRequestService service,
            CancellationToken ct) =>
        {
            try
            {
                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);

                bool activeOnly = false;
                ServiceRequestStatus? statusFilter = null;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    if (string.Equals(status.Trim(), "active", StringComparison.OrdinalIgnoreCase))
                    {
                        activeOnly = true;
                    }
                    else if (Enum.TryParse<ServiceRequestStatus>(status, true, out var parsedStatus))
                    {
                        statusFilter = parsedStatus;
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid status filter '{status}'.");
                    }
                }

                var list = await service.ListServiceRequestsAsync(
                    tenantId,
                    new BranchId(branchId),
                    statusFilter,
                    null,
                    new DiningSessionId(sessionId),
                    100,
                    0,
                    actor,
                    activeOnly,
                    ct);

                return Results.Ok(list);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("ListStaffSessionServiceRequests")
        .WithSummary("List service requests for an active dining session");

        return app;
    }
}
