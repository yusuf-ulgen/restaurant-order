using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;

namespace RestaurantOrder.Api.Catalog;

public sealed record CreateMenuApiRequest(
    string Name,
    string Slug,
    string? Description = null,
    int SortOrder = 0);

public sealed record UpdateMenuApiRequest(
    string Name,
    string? Description = null,
    int SortOrder = 0,
    Guid? ConcurrencyToken = null);

public sealed record MenuStateApiRequest(
    Guid? ConcurrencyToken = null);

public static partial class CatalogEndpoints
{
    private static void MapMenuEndpoints(RouteGroupBuilder branchGroup)
    {
        branchGroup.MapGet("/menus", ListMenusHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("ListMenus");

        branchGroup.MapGet("/menus/{menuId:guid}", GetMenuHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("GetMenu");

        branchGroup.MapPost("/menus", CreateMenuHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("CreateMenu");

        branchGroup.MapPut("/menus/{menuId:guid}", UpdateMenuHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("UpdateMenu");

        branchGroup.MapPost("/menus/{menuId:guid}/activate", ActivateMenuHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ActivateMenu");

        branchGroup.MapPost("/menus/{menuId:guid}/archive", ArchiveMenuHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ArchiveMenu");
    }

    internal static async Task<IResult> ListMenusHandler(
        Guid branchId,
        HttpContext httpContext,
        ICatalogService service,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var result = await service.ListMenusAsync(tenantId, new BranchId(branchId), actor, ct);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> GetMenuHandler(
        Guid branchId,
        Guid menuId,
        HttpContext httpContext,
        ICatalogService service,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var result = await service.GetMenuAsync(tenantId, new BranchId(branchId), new MenuId(menuId), actor, ct);
            return MenuResult(httpContext, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> CreateMenuHandler(
        Guid branchId,
        CreateMenuApiRequest request,
        HttpContext httpContext,
        ICatalogService service,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var command = new CreateMenuCommand(
                Name: request.Name,
                Slug: request.Slug,
                Description: request.Description,
                SortOrder: request.SortOrder);

            var result = await service.CreateMenuAsync(tenantId, new BranchId(branchId), command, actor, ct);
            return MenuResult(httpContext, result, StatusCodes.Status201Created);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> UpdateMenuHandler(
        Guid branchId,
        Guid menuId,
        UpdateMenuApiRequest request,
        HttpContext httpContext,
        ICatalogService service,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);

            var command = new UpdateMenuCommand(
                Name: request.Name,
                Description: request.Description,
                SortOrder: request.SortOrder,
                ConcurrencyToken: token);

            var result = await service.UpdateMenuAsync(tenantId, new BranchId(branchId), new MenuId(menuId), command, actor, ct);
            return MenuResult(httpContext, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ActivateMenuHandler(
        Guid branchId,
        Guid menuId,
        MenuStateApiRequest? request,
        HttpContext httpContext,
        ICatalogService service,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);

            var result = await service.ActivateMenuAsync(tenantId, new BranchId(branchId), new MenuId(menuId), token, actor, ct);
            return MenuResult(httpContext, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ArchiveMenuHandler(
        Guid branchId,
        Guid menuId,
        MenuStateApiRequest? request,
        HttpContext httpContext,
        ICatalogService service,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);

            var result = await service.ArchiveMenuAsync(tenantId, new BranchId(branchId), new MenuId(menuId), token, actor, ct);
            return MenuResult(httpContext, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }
}
