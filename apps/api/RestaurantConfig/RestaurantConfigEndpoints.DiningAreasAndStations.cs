using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Api.RestaurantConfig;

public sealed record CreateDiningAreaApiRequest(
    string Name,
    string Code,
    string AreaType,
    int SortOrder = 0);

public sealed record UpdateDiningAreaApiRequest(
    string Name,
    string AreaType,
    Guid? ConcurrencyToken = null);

public sealed record ReorderDiningAreasApiRequest(
    IReadOnlyList<Guid> OrderedAreaIds);

public sealed record CreatePreparationStationApiRequest(
    string Code,
    string DisplayName,
    string StationType,
    int SortOrder = 0);

public sealed record UpdatePreparationStationApiRequest(
    string DisplayName,
    string StationType,
    Guid? ConcurrencyToken = null);

public sealed record ReorderPreparationStationsApiRequest(
    IReadOnlyList<Guid> OrderedStationIds);

public static partial class RestaurantConfigEndpoints
{
    private static void MapDiningAreasAndStationsEndpoints(RouteGroupBuilder root)
    {
        var branchGroup = root.MapGroup("/branches/{branchId:guid}");

        // ==========================================
        // Dining Areas
        // ==========================================

        branchGroup.MapGet("/dining-areas", async (
            Guid branchId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var areas = await service.ListDiningAreasAsync(tenantId, new BranchId(branchId), actor, ct);
                return Results.Ok(areas);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationView)
        .WithName("ListDiningAreas");

        branchGroup.MapPost("/dining-areas", async (
            Guid branchId,
            CreateDiningAreaApiRequest request,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new CreateDiningAreaCommand(request.Name, request.Code, request.AreaType, request.SortOrder);
                var created = await service.CreateDiningAreaAsync(tenantId, new BranchId(branchId), command, actor, ct);

                httpContext.Response.Headers.ETag = $"\"{created.ConcurrencyToken:D}\"";
                return Results.Created($"/api/v1/restaurant-config/branches/{branchId}/dining-areas/{created.Id}", created);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("CreateDiningArea");

        branchGroup.MapPut("/dining-areas/{areaId:guid}", async (
            Guid branchId,
            Guid areaId,
            UpdateDiningAreaApiRequest request,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for updates.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new UpdateDiningAreaCommand(request.Name, request.AreaType, token);
                var updated = await service.UpdateDiningAreaAsync(tenantId, new BranchId(branchId), new DiningAreaId(areaId), command, actor, ct);

                httpContext.Response.Headers.ETag = $"\"{updated.ConcurrencyToken:D}\"";
                return Results.Ok(updated);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("UpdateDiningArea");

        branchGroup.MapPost("/dining-areas/reorder", async (
            Guid branchId,
            ReorderDiningAreasApiRequest request,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new ReorderDiningAreasCommand(request.OrderedAreaIds);
                var result = await service.ReorderDiningAreasAsync(tenantId, new BranchId(branchId), command, actor, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("ReorderDiningAreas");

        branchGroup.MapPost("/dining-areas/{areaId:guid}/activate", async (
            Guid branchId,
            Guid areaId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var result = await service.ActivateDiningAreaAsync(tenantId, new BranchId(branchId), new DiningAreaId(areaId), actor, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("ActivateDiningArea");

        branchGroup.MapPost("/dining-areas/{areaId:guid}/deactivate", async (
            Guid branchId,
            Guid areaId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var result = await service.DeactivateDiningAreaAsync(tenantId, new BranchId(branchId), new DiningAreaId(areaId), actor, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("DeactivateDiningArea");

        // ==========================================
        // Preparation Stations
        // ==========================================

        branchGroup.MapGet("/stations", async (
            Guid branchId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var stations = await service.ListPreparationStationsAsync(tenantId, new BranchId(branchId), actor, ct);
                return Results.Ok(stations);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationView)
        .WithName("ListPreparationStations");

        branchGroup.MapGet("/stations/runtime", async (
            Guid branchId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var stations = await service.GetPreparationStationRuntimeAsync(tenantId, new BranchId(branchId), actor, ct);
                return Results.Ok(stations);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .WithName("GetPreparationStationRuntime");

        branchGroup.MapPost("/stations", async (
            Guid branchId,
            CreatePreparationStationApiRequest request,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new CreatePreparationStationCommand(request.Code, request.DisplayName, request.StationType, request.SortOrder);
                var created = await service.CreatePreparationStationAsync(tenantId, new BranchId(branchId), command, actor, ct);

                httpContext.Response.Headers.ETag = $"\"{created.ConcurrencyToken:D}\"";
                return Results.Created($"/api/v1/restaurant-config/branches/{branchId}/stations/{created.Id}", created);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("CreatePreparationStation");

        branchGroup.MapPut("/stations/{stationId:guid}", async (
            Guid branchId,
            Guid stationId,
            UpdatePreparationStationApiRequest request,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for updates.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new UpdatePreparationStationCommand(request.DisplayName, request.StationType, token);
                var updated = await service.UpdatePreparationStationAsync(tenantId, new BranchId(branchId), new PreparationStationId(stationId), command, actor, ct);

                httpContext.Response.Headers.ETag = $"\"{updated.ConcurrencyToken:D}\"";
                return Results.Ok(updated);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("UpdatePreparationStation");

        branchGroup.MapPost("/stations/reorder", async (
            Guid branchId,
            ReorderPreparationStationsApiRequest request,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new ReorderPreparationStationsCommand(request.OrderedStationIds);
                var result = await service.ReorderPreparationStationsAsync(tenantId, new BranchId(branchId), command, actor, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("ReorderPreparationStations");

        branchGroup.MapPost("/stations/{stationId:guid}/activate", async (
            Guid branchId,
            Guid stationId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var result = await service.ActivatePreparationStationAsync(tenantId, new BranchId(branchId), new PreparationStationId(stationId), actor, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("ActivatePreparationStation");

        branchGroup.MapPost("/stations/{stationId:guid}/deactivate", async (
            Guid branchId,
            Guid stationId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var result = await service.DeactivatePreparationStationAsync(tenantId, new BranchId(branchId), new PreparationStationId(stationId), actor, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("DeactivatePreparationStation");
    }

    internal static IResult HandleException(Exception ex) => ex switch
    {
        ResourceNotFoundException rnfe => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: rnfe.Message, type: "https://httpstatuses.com/404"),
        DuplicateCodeException dce => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: dce.Message, type: "https://httpstatuses.com/409"),
        DuplicateSlugException dse => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: dse.Message, type: "https://httpstatuses.com/409"),
        ConcurrencyConflictException cce => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: cce.Message, type: "https://httpstatuses.com/409"),
        InvalidAuthorizationScopeException iase => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: iase.Message, type: "https://httpstatuses.com/403"),
        DomainException de => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: de.Message, type: "https://httpstatuses.com/400"),
        ArgumentException ae => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ae.Message, type: "https://httpstatuses.com/400"),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error", detail: ex.Message, type: "https://httpstatuses.com/500")
    };
}
