using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;

namespace RestaurantOrder.Api.Catalog;

public static partial class CatalogEndpoints
{
    private static void MapItemModifierEndpoints(RouteGroupBuilder branchGroup)
    {
        var itemRoute = branchGroup.MapGroup("/menus/{menuId:guid}/items/{itemId:guid}");

        itemRoute.MapPut("/metadata", UpdateItemMetadataHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("UpdateItemMetadata");

        itemRoute.MapPost("/modifier-groups", AssignModifierGroupHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("AssignModifierGroup");

        itemRoute.MapDelete("/modifier-groups/{groupId:guid}", RemoveModifierGroupHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("RemoveModifierGroup");

        itemRoute.MapPost("/modifier-groups/reorder", ReorderItemModifierGroupsHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ReorderItemModifierGroups");
    }

    internal static async Task<IResult> UpdateItemMetadataHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        UpdateMenuItemMetadataCommand command,
        HttpContext httpContext,
        ICatalogService catalogService,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(command.ConcurrencyToken, httpContext.Request);

            var updated = await catalogService.UpdateItemMetadataAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                command with { ConcurrencyToken = token },
                actor,
                cancellationToken);

            return ItemResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> AssignModifierGroupHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        AssignModifierGroupCommand command,
        HttpContext httpContext,
        ICatalogService catalogService,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(command.ConcurrencyToken, httpContext.Request);

            var updated = await catalogService.AssignModifierGroupAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                command with { ConcurrencyToken = token },
                actor,
                cancellationToken);

            return ItemResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> RemoveModifierGroupHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        Guid groupId,
        HttpContext httpContext,
        ICatalogService catalogService,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(null, httpContext.Request);

            var updated = await catalogService.RemoveModifierGroupAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                new ModifierGroupId(groupId),
                token,
                actor,
                cancellationToken);

            return ItemResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ReorderItemModifierGroupsHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        ReorderMenuItemModifierGroupsCommand command,
        HttpContext httpContext,
        ICatalogService catalogService,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = GetActor(httpContext, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(command.ConcurrencyToken, httpContext.Request);

            var updated = await catalogService.ReorderMenuItemModifierGroupsAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                command with { ConcurrencyToken = token },
                actor,
                cancellationToken);

            return ItemResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }
}
