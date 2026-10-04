using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Api.RestaurantConfig;

public sealed record CreatePreparationStationApiRequest(
    string Code,
    string DisplayName,
    string StationType,
    int SortOrder = 0);

public sealed record UpdatePreparationStationApiRequest(
    string DisplayName,
    string StationType,
    Guid? ConcurrencyToken = null);

public sealed record PreparationStationStateApiRequest(
    Guid? ConcurrencyToken = null);

public sealed record ReorderPreparationStationsApiRequest(
    IReadOnlyList<ReorderItemApiRequest>? Items = null,
    IReadOnlyList<Guid>? OrderedStationIds = null);

public static partial class RestaurantConfigEndpoints
{
    private static void MapPreparationStationsEndpoints(RouteGroupBuilder root)
    {
        var branchGroup = root.MapGroup("/branches/{branchId:guid}");

        branchGroup.MapGet("/stations", ListPreparationStationsHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationView)
            .WithName("ListPreparationStations");

        branchGroup.MapGet("/stations/runtime", GetPreparationStationRuntimeHandler)
            .RequireAuthorization()
            .WithName("GetPreparationStationRuntime");

        branchGroup.MapPost("/stations", CreatePreparationStationHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationManage)
            .WithName("CreatePreparationStation");

        branchGroup.MapPut("/stations/{stationId:guid}", UpdatePreparationStationHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationManage)
            .WithName("UpdatePreparationStation");

        branchGroup.MapPost("/stations/reorder", ReorderPreparationStationsHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationManage)
            .WithName("ReorderPreparationStations");

        branchGroup.MapPost("/stations/{stationId:guid}/activate", ActivatePreparationStationHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationManage)
            .WithName("ActivatePreparationStation");

        branchGroup.MapPost("/stations/{stationId:guid}/deactivate", DeactivatePreparationStationHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationManage)
            .WithName("DeactivatePreparationStation");
    }

    internal static async Task<IResult> ListPreparationStationsHandler(
        Guid branchId,
        ITenantContext tenantContext,
        IRestaurantConfigService service,
        HttpContext httpContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var stations = await service.ListPreparationStationsAsync(tenantId, new BranchId(branchId), actor, ct);
            return Results.Ok(stations);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> GetPreparationStationRuntimeHandler(
        Guid branchId,
        ITenantContext tenantContext,
        IRestaurantConfigService service,
        HttpContext httpContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var stations = await service.GetPreparationStationRuntimeAsync(tenantId, new BranchId(branchId), actor, ct);
            return Results.Ok(stations);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> CreatePreparationStationHandler(
        Guid branchId,
        CreatePreparationStationApiRequest request,
        ITenantContext tenantContext,
        IRestaurantConfigService service,
        HttpContext httpContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var command = new CreatePreparationStationCommand(request.Code, request.DisplayName, request.StationType, request.SortOrder);
            var created = await service.CreatePreparationStationAsync(tenantId, new BranchId(branchId), command, actor, ct);

            httpContext.Response.Headers.ETag = $"\"{created.ConcurrencyToken:D}\"";
            return Results.Created($"/api/v1/restaurant-config/branches/{branchId}/stations/{created.Id}", created);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> UpdatePreparationStationHandler(
        Guid branchId,
        Guid stationId,
        UpdatePreparationStationApiRequest request,
        ITenantContext tenantContext,
        IRestaurantConfigService service,
        HttpContext httpContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
        if (token == null)
        {
            return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for updates.", type: "https://httpstatuses.com/412");
        }

        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var command = new UpdatePreparationStationCommand(request.DisplayName, request.StationType, token);
            var updated = await service.UpdatePreparationStationAsync(tenantId, new BranchId(branchId), new PreparationStationId(stationId), command, actor, ct);

            httpContext.Response.Headers.ETag = $"\"{updated.ConcurrencyToken:D}\"";
            return Results.Ok(updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> ReorderPreparationStationsHandler(
        Guid branchId,
        ReorderPreparationStationsApiRequest request,
        ITenantContext tenantContext,
        IRestaurantConfigService service,
        HttpContext httpContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        var items = request.Items;
        if (items == null && request.OrderedStationIds != null)
        {
            return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token is required for all reordered items.", type: "https://httpstatuses.com/412");
        }

        if (items == null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: "Reorder items list is required.", type: "https://httpstatuses.com/400");
        }

        if (items.Any(i => !i.ConcurrencyToken.HasValue || i.ConcurrencyToken.Value == Guid.Empty))
        {
            return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token is required for all reordered items.", type: "https://httpstatuses.com/412");
        }

        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var command = new ReorderPreparationStationsCommand(items.Select(x => new ReorderItemCommand(x.Id, x.ConcurrencyToken)).ToList());
            var result = await service.ReorderPreparationStationsAsync(tenantId, new BranchId(branchId), command, actor, ct);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> ActivatePreparationStationHandler(
        Guid branchId,
        Guid stationId,
        PreparationStationStateApiRequest? request,
        ITenantContext tenantContext,
        IRestaurantConfigService service,
        HttpContext httpContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);
        if (token == null)
        {
            return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required.", type: "https://httpstatuses.com/412");
        }

        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var result = await service.ActivatePreparationStationAsync(tenantId, new BranchId(branchId), new PreparationStationId(stationId), token.Value, actor, ct);
            httpContext.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> DeactivatePreparationStationHandler(
        Guid branchId,
        Guid stationId,
        PreparationStationStateApiRequest? request,
        ITenantContext tenantContext,
        IRestaurantConfigService service,
        HttpContext httpContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);
        if (token == null)
        {
            return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required.", type: "https://httpstatuses.com/412");
        }

        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var result = await service.DeactivatePreparationStationAsync(tenantId, new BranchId(branchId), new PreparationStationId(stationId), token.Value, actor, ct);
            httpContext.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }
}
