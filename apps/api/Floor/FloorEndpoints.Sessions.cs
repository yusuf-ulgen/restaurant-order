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

public static class FloorSessionEndpoints
{
    public static RouteGroupBuilder MapFloorSessionEndpoints(this RouteGroupBuilder branchGroup)
    {
        branchGroup.MapGet("/status", async (
            Guid branchId,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
                var status = await floorService.GetBranchFloorStatusAsync(
                    tenantId,
                    new BranchId(branchId),
                    actor,
                    ct);
                return Results.Ok(status);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("GetBranchFloorStatus")
        .WithSummary("Get real-time floor occupancy and table status for a branch");

        branchGroup.MapGet("/tables/{tableId:guid}/session", async (
            Guid branchId,
            Guid tableId,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
                var session = await floorService.GetActiveSessionForTableAsync(
                    tenantId,
                    new BranchId(branchId),
                    new RestaurantTableId(tableId),
                    actor,
                    ct);

                return session != null
                    ? FloorEndpointHelpers.SessionResult(context, session)
                    : Results.NotFound();
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("GetActiveTableSession")
        .WithSummary("Get current active dining session for a table");

        branchGroup.MapGet("/tables/{tableId:guid}/sessions", async (
            Guid branchId,
            Guid tableId,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
                var sessions = await floorService.ListSessionsForTableAsync(
                    tenantId,
                    new BranchId(branchId),
                    new RestaurantTableId(tableId),
                    actor,
                    ct);
                return Results.Ok(sessions);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("ListTableSessions")
        .WithSummary("List dining session history for a table");

        branchGroup.MapGet("/sessions/{sessionId:guid}", async (
            Guid branchId,
            Guid sessionId,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
                var session = await floorService.GetSessionAsync(
                    tenantId,
                    new BranchId(branchId),
                    new DiningSessionId(sessionId),
                    actor,
                    ct);
                return FloorEndpointHelpers.SessionResult(context, session);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("GetSessionById")
        .WithSummary("Get dining session by identifier");

        branchGroup.MapPost("/tables/{tableId:guid}/sessions", async (
            Guid branchId,
            Guid tableId,
            [FromBody] OpenDiningSessionRequest request,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
                var session = await floorService.OpenSessionAsync(
                    tenantId,
                    new BranchId(branchId),
                    new RestaurantTableId(tableId),
                    request,
                    actor,
                    ct);
                return FloorEndpointHelpers.SessionResult(context, session, StatusCodes.Status201Created);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("OpenTableSession")
        .WithSummary("Open a new dining session at a table");

        branchGroup.MapPost("/sessions/{sessionId:guid}/activate", async (
            Guid branchId,
            Guid sessionId,
            [FromBody] TransitionSessionRequest? request,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
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
                var session = await floorService.ActivateSessionAsync(
                    tenantId,
                    new BranchId(branchId),
                    new DiningSessionId(sessionId),
                    token,
                    actor,
                    ct);
                return FloorEndpointHelpers.SessionResult(context, session);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("ActivateSession")
        .WithSummary("Transition dining session from Open to Active");

        branchGroup.MapPost("/sessions/{sessionId:guid}/request-bill", async (
            Guid branchId,
            Guid sessionId,
            [FromBody] TransitionSessionRequest? request,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
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
                var session = await floorService.RequestBillAsync(
                    tenantId,
                    new BranchId(branchId),
                    new DiningSessionId(sessionId),
                    token,
                    actor,
                    ct);
                return FloorEndpointHelpers.SessionResult(context, session);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("RequestSessionBill")
        .WithSummary("Transition dining session from Active to BillRequested");

        branchGroup.MapPost("/sessions/{sessionId:guid}/close", async (
            Guid branchId,
            Guid sessionId,
            [FromBody] CloseDiningSessionRequest? request,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
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
                var session = await floorService.CloseSessionAsync(
                    tenantId,
                    new BranchId(branchId),
                    new DiningSessionId(sessionId),
                    request ?? new CloseDiningSessionRequest(),
                    token,
                    actor,
                    ct);
                return FloorEndpointHelpers.SessionResult(context, session);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("CloseSession")
        .WithSummary("Transition dining session from BillRequested to Closed");

        return branchGroup;
    }
}
