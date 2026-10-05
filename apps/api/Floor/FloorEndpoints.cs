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

public static class FloorEndpoints
{
    public static IEndpointRouteBuilder MapFloorEndpoints(this IEndpointRouteBuilder app)
    {
        var branchGroup = app.MapGroup("/api/v1/floor/branches/{branchId:guid}")
            .WithTags("Floor");

        branchGroup.MapGet("/tables", async (
            Guid branchId,
            [FromQuery] Guid? diningAreaId,
            [FromQuery] bool? isActive,
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
                var tables = await floorService.ListTablesAsync(
                    tenantId,
                    new BranchId(branchId),
                    diningAreaId,
                    isActive,
                    actor,
                    ct);
                return Results.Ok(tables);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("GetTables")
        .WithSummary("List tables for a branch");

        branchGroup.MapGet("/tables/{tableId:guid}", async (
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
                var table = await floorService.GetTableAsync(
                    tenantId,
                    new BranchId(branchId),
                    new RestaurantTableId(tableId),
                    actor,
                    ct);
                return FloorEndpointHelpers.TableResult(context, table);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("GetTableById")
        .WithSummary("Get a table by ID");

        branchGroup.MapPost("/tables", async (
            Guid branchId,
            [FromBody] CreateTableApiRequest request,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                if (request.BranchId.HasValue && request.BranchId.Value != branchId)
                {
                    throw new ArgumentException("Route branchId does not match request payload branchId.");
                }

                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
                var appRequest = new CreateTableRequest(
                    request.DiningAreaId,
                    request.TableNumber,
                    request.DisplayName,
                    request.Capacity,
                    request.PositionX,
                    request.PositionY,
                    request.Width,
                    request.Height,
                    request.RotationDegrees,
                    request.Shape);

                var table = await floorService.CreateTableAsync(
                    tenantId,
                    new BranchId(branchId),
                    appRequest,
                    actor,
                    ct);
                return FloorEndpointHelpers.TableResult(context, table, StatusCodes.Status201Created);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("CreateTable")
        .WithSummary("Create a new table");

        branchGroup.MapPut("/tables/{tableId:guid}", async (
            Guid branchId,
            Guid tableId,
            [FromBody] UpdateTableApiRequest request,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                if (request.BranchId.HasValue && request.BranchId.Value != branchId)
                {
                    throw new ArgumentException("Route branchId does not match request payload branchId.");
                }

                var token = FloorEndpointHelpers.ExtractConcurrencyToken(request.ConcurrencyToken, context.Request);
                if (!token.HasValue)
                {
                    throw new ConcurrencyPreconditionException("If-Match header or concurrency token in request body is required.");
                }

                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
                var appRequest = new UpdateTableRequest(
                    request.DiningAreaId,
                    request.TableNumber,
                    request.DisplayName,
                    request.Capacity,
                    token.Value);

                var table = await floorService.UpdateTableAsync(
                    tenantId,
                    new BranchId(branchId),
                    new RestaurantTableId(tableId),
                    appRequest,
                    token.Value,
                    actor,
                    ct);
                return FloorEndpointHelpers.TableResult(context, table);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("UpdateTable")
        .WithSummary("Update table basic information");

        branchGroup.MapPut("/tables/{tableId:guid}/layout", async (
            Guid branchId,
            Guid tableId,
            [FromBody] UpdateTableLayoutApiRequest request,
            HttpContext context,
            IFloorService floorService,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var token = FloorEndpointHelpers.ExtractConcurrencyToken(request.ConcurrencyToken, context.Request);
                if (!token.HasValue)
                {
                    throw new ConcurrencyPreconditionException("If-Match header or concurrency token in request body is required.");
                }

                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
                var appRequest = new UpdateTableLayoutRequest(
                    request.PositionX,
                    request.PositionY,
                    request.Width,
                    request.Height,
                    request.RotationDegrees,
                    request.Shape,
                    token.Value);

                var table = await floorService.UpdateTableLayoutAsync(
                    tenantId,
                    new BranchId(branchId),
                    new RestaurantTableId(tableId),
                    appRequest,
                    token.Value,
                    actor,
                    ct);
                return FloorEndpointHelpers.TableResult(context, table);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("UpdateTableLayout")
        .WithSummary("Update table physical layout");

        branchGroup.MapPost("/tables/{tableId:guid}/activate", async (
            Guid branchId,
            Guid tableId,
            [FromBody] TableStateApiRequest? request,
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
                    throw new ConcurrencyPreconditionException("If-Match header or concurrency token in request body is required.");
                }

                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
                var table = await floorService.ActivateTableAsync(
                    tenantId,
                    new BranchId(branchId),
                    new RestaurantTableId(tableId),
                    token.Value,
                    actor,
                    ct);
                return FloorEndpointHelpers.TableResult(context, table);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("ActivateTable")
        .WithSummary("Activate a deactivated table");

        branchGroup.MapPost("/tables/{tableId:guid}/deactivate", async (
            Guid branchId,
            Guid tableId,
            [FromBody] TableStateApiRequest? request,
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
                    throw new ConcurrencyPreconditionException("If-Match header or concurrency token in request body is required.");
                }

                var actor = FloorEndpointHelpers.GetActor(context, parser);
                var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
                var table = await floorService.DeactivateTableAsync(
                    tenantId,
                    new BranchId(branchId),
                    new RestaurantTableId(tableId),
                    token.Value,
                    actor,
                    ct);
                return FloorEndpointHelpers.TableResult(context, table);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("DeactivateTable")
        .WithSummary("Deactivate a table");

        branchGroup.MapFloorSessionEndpoints();
        branchGroup.MapFloorQrEndpoints();

        app.MapQrCustomerEndpoints();

        return app;
    }
}
