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

public static class FloorQrEndpoints
{
    public static RouteGroupBuilder MapFloorQrEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/tables/{tableId:guid}/qr", async (
            Guid branchId,
            Guid tableId,
            [FromQuery] string? format,
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
                var qr = await floorService.GenerateTableStaticQrAsync(
                    tenantId,
                    new BranchId(branchId),
                    new RestaurantTableId(tableId),
                    actor,
                    ct);

                if (string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Content(qr.Svg, "image/svg+xml");
                }

                return Results.Ok(qr);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("GetTableStaticQr")
        .WithSummary("Generate or preview static signed QR for a table");

        group.MapGet("/tables/{tableId:guid}/qr/metadata", async (
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
                var metadata = await floorService.GetTableQrMetadataAsync(
                    tenantId,
                    new BranchId(branchId),
                    new RestaurantTableId(tableId),
                    actor,
                    ct);

                return Results.Ok(metadata);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("GetTableQrMetadata")
        .WithSummary("View table QR security metadata without secrets");

        group.MapPost("/tables/{tableId:guid}/qr/rotate", async (
            Guid branchId,
            Guid tableId,
            [FromBody] RotateQrVersionRequest? request,
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
                var table = await floorService.RotateTableQrVersionAsync(
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
        .WithName("RotateTableQrVersion")
        .WithSummary("Rotate and revoke table QR version");

        group.MapPost("/sessions/{sessionId:guid}/qr", async (
            Guid branchId,
            Guid sessionId,
            [FromQuery] string? format,
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
                var qr = await floorService.GenerateSessionDynamicQrAsync(
                    tenantId,
                    new BranchId(branchId),
                    new DiningSessionId(sessionId),
                    actor,
                    ct);

                if (string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Content(qr.Svg, "image/svg+xml");
                }

                return Results.Ok(qr);
            }
            catch (Exception ex)
            {
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .WithName("GenerateSessionDynamicQr")
        .WithSummary("Generate short-lived dynamic QR for an active session");

        return group;
    }
}
