using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
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

public sealed record DiningAreaStateApiRequest(
    Guid? ConcurrencyToken = null);

public sealed record ReorderItemApiRequest(
    Guid Id,
    Guid? ConcurrencyToken = null);

public sealed record ReorderDiningAreasApiRequest(
    IReadOnlyList<ReorderItemApiRequest>? Items = null,
    IReadOnlyList<Guid>? OrderedAreaIds = null);

public static partial class RestaurantConfigEndpoints
{
    private static void MapDiningAreasEndpoints(RouteGroupBuilder root)
    {
        var branchGroup = root.MapGroup("/branches/{branchId:guid}");

        branchGroup.MapGet("/dining-areas", ListDiningAreasHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationView)
            .WithName("ListDiningAreas");

        branchGroup.MapPost("/dining-areas", CreateDiningAreaHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationManage)
            .WithName("CreateDiningArea");

        branchGroup.MapPut("/dining-areas/{areaId:guid}", UpdateDiningAreaHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationManage)
            .WithName("UpdateDiningArea");

        branchGroup.MapPost("/dining-areas/reorder", ReorderDiningAreasHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationManage)
            .WithName("ReorderDiningAreas");

        branchGroup.MapPost("/dining-areas/{areaId:guid}/activate", ActivateDiningAreaHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationManage)
            .WithName("ActivateDiningArea");

        branchGroup.MapPost("/dining-areas/{areaId:guid}/deactivate", DeactivateDiningAreaHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.BranchConfigurationManage)
            .WithName("DeactivateDiningArea");
    }

    internal static async Task<IResult> ListDiningAreasHandler(
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
            var areas = await service.ListDiningAreasAsync(tenantId, new BranchId(branchId), actor, ct);
            return Results.Ok(areas);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> CreateDiningAreaHandler(
        Guid branchId,
        CreateDiningAreaApiRequest request,
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
            var command = new CreateDiningAreaCommand(request.Name, request.Code, request.AreaType, request.SortOrder);
            var created = await service.CreateDiningAreaAsync(tenantId, new BranchId(branchId), command, actor, ct);

            httpContext.Response.Headers.ETag = $"\"{created.ConcurrencyToken:D}\"";
            return Results.Created($"/api/v1/restaurant-config/branches/{branchId}/dining-areas/{created.Id}", created);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> UpdateDiningAreaHandler(
        Guid branchId,
        Guid areaId,
        UpdateDiningAreaApiRequest request,
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
            var command = new UpdateDiningAreaCommand(request.Name, request.AreaType, token);
            var updated = await service.UpdateDiningAreaAsync(tenantId, new BranchId(branchId), new DiningAreaId(areaId), command, actor, ct);

            httpContext.Response.Headers.ETag = $"\"{updated.ConcurrencyToken:D}\"";
            return Results.Ok(updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> ReorderDiningAreasHandler(
        Guid branchId,
        ReorderDiningAreasApiRequest request,
        ITenantContext tenantContext,
        IRestaurantConfigService service,
        HttpContext httpContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        var items = request.Items;
        if (items == null && request.OrderedAreaIds != null)
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
            var command = new ReorderDiningAreasCommand(items.Select(x => new ReorderItemCommand(x.Id, x.ConcurrencyToken)).ToList());
            var result = await service.ReorderDiningAreasAsync(tenantId, new BranchId(branchId), command, actor, ct);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> ActivateDiningAreaHandler(
        Guid branchId,
        Guid areaId,
        DiningAreaStateApiRequest? request,
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
            var result = await service.ActivateDiningAreaAsync(tenantId, new BranchId(branchId), new DiningAreaId(areaId), token.Value, actor, ct);
            httpContext.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    internal static async Task<IResult> DeactivateDiningAreaHandler(
        Guid branchId,
        Guid areaId,
        DiningAreaStateApiRequest? request,
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
            var result = await service.DeactivateDiningAreaAsync(tenantId, new BranchId(branchId), new DiningAreaId(areaId), token.Value, actor, ct);
            httpContext.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }
}
