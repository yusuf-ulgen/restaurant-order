using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Api.Catalog;

public sealed record CreateMenuItemApiRequest(
    Guid CategoryId,
    string Name,
    string Slug,
    long BasePriceMinorUnits,
    string? ShortDescription = null,
    string? FullDescription = null,
    string? ImageUrl = null,
    int SortOrder = 0,
    Guid? PreparationStationId = null);

public sealed record UpdateMenuItemApiRequest(
    Guid CategoryId,
    string Name,
    string? ShortDescription = null,
    string? FullDescription = null,
    string? ImageUrl = null,
    int SortOrder = 0,
    long? BasePriceMinorUnits = null,
    Guid? ConcurrencyToken = null);

public sealed record UpdateMenuItemPriceApiRequest(
    long BasePriceMinorUnits,
    Guid? ConcurrencyToken = null);

public sealed record MenuItemStateApiRequest(
    Guid? ConcurrencyToken = null);

public sealed record ItemReorderItemApiRequest(
    Guid Id,
    int SortOrder,
    Guid? ConcurrencyToken = null);

public sealed record ReorderMenuItemsApiRequest(
    IReadOnlyList<ItemReorderItemApiRequest> Items);

public static partial class CatalogEndpoints
{
    private static void MapItemEndpoints(RouteGroupBuilder branchGroup)
    {
        var itemGroup = branchGroup.MapGroup("/menus/{menuId:guid}/items");

        itemGroup.MapGet("", ListItemsHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("ListMenuItems");

        itemGroup.MapGet("/{itemId:guid}", GetItemHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("GetMenuItem");

        itemGroup.MapPost("", CreateItemHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("CreateMenuItem");

        itemGroup.MapPut("/{itemId:guid}", UpdateItemHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("UpdateMenuItem");

        itemGroup.MapPut("/{itemId:guid}/price", UpdateItemPriceHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuPricingManage)
            .WithName("UpdateMenuItemPrice");

        itemGroup.MapPost("/{itemId:guid}/activate", ActivateItemHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ActivateMenuItem");

        itemGroup.MapPost("/{itemId:guid}/deactivate", DeactivateItemHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("DeactivateMenuItem");

        itemGroup.MapPost("/categories/{categoryId:guid}/reorder", ReorderItemsHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ReorderMenuItems");
    }

    internal static async Task<IResult> ListItemsHandler(
        Guid branchId,
        Guid menuId,
        Guid? categoryId,
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

            var items = await catalogService.ListItemsAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                categoryId.HasValue ? new MenuCategoryId(categoryId.Value) : null,
                actor,
                cancellationToken);

            return Results.Ok(items);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> GetItemHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
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

            var item = await catalogService.GetItemAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                actor,
                cancellationToken);

            return ItemResult(httpContext, item);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> CreateItemHandler(
        Guid branchId,
        Guid menuId,
        CreateMenuItemApiRequest request,
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

            var command = new CreateMenuItemCommand(
                CategoryId: request.CategoryId,
                Name: request.Name,
                Slug: request.Slug,
                BasePriceMinorUnits: request.BasePriceMinorUnits,
                ShortDescription: request.ShortDescription,
                FullDescription: request.FullDescription,
                ImageUrl: request.ImageUrl,
                SortOrder: request.SortOrder,
                PreparationStationId: request.PreparationStationId);

            var created = await catalogService.CreateItemAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                command,
                actor,
                cancellationToken);

            return ItemResult(httpContext, created, StatusCodes.Status201Created);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> UpdateItemHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        UpdateMenuItemApiRequest request,
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
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);

            var command = new UpdateMenuItemCommand(
                CategoryId: request.CategoryId,
                Name: request.Name,
                ShortDescription: request.ShortDescription,
                FullDescription: request.FullDescription,
                ImageUrl: request.ImageUrl,
                SortOrder: request.SortOrder,
                BasePriceMinorUnits: request.BasePriceMinorUnits,
                ConcurrencyToken: token);

            var updated = await catalogService.UpdateItemAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                command,
                actor,
                cancellationToken);

            return ItemResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> UpdateItemPriceHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        UpdateMenuItemPriceApiRequest request,
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
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);

            var command = new UpdateMenuItemPriceCommand(
                BasePriceMinorUnits: request.BasePriceMinorUnits,
                ConcurrencyToken: token);

            var updated = await catalogService.UpdateItemPriceAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                command,
                actor,
                cancellationToken);

            return ItemResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ActivateItemHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        MenuItemStateApiRequest? request,
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
            var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);

            var updated = await catalogService.ActivateItemAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
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

    internal static async Task<IResult> DeactivateItemHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        MenuItemStateApiRequest? request,
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
            var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);

            var updated = await catalogService.DeactivateItemAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
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

    internal static async Task<IResult> ReorderItemsHandler(
        Guid branchId,
        Guid menuId,
        Guid categoryId,
        ReorderMenuItemsApiRequest request,
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

            if (request.Items == null || request.Items.Count == 0)
            {
                throw new DomainException("Reorder items cannot be empty.");
            }

            var items = new List<ItemReorderItem>();
            foreach (var item in request.Items)
            {
                if (!item.ConcurrencyToken.HasValue || item.ConcurrencyToken.Value == Guid.Empty)
                {
                    throw new ConcurrencyPreconditionException(
                        $"Concurrency token is required for item '{item.Id}' in reorder list.");
                }

                items.Add(new ItemReorderItem(item.Id, item.SortOrder, item.ConcurrencyToken.Value));
            }

            var command = new ReorderMenuItemsCommand(items);
            var result = await catalogService.ReorderItemsAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuCategoryId(categoryId),
                command,
                actor,
                cancellationToken);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }
}
