using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;

namespace RestaurantOrder.Api.Catalog;

public static partial class CatalogEndpoints
{
    private static void MapAvailabilityEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/availability", ListBranchAvailabilitiesHandler)
            .WithName("ListBranchAvailabilities")
            .RequireAuthorization("Permission:menu.catalog.view");

        group.MapPost("/menus/{menuId:guid}/items/{itemId:guid}/quick-86", Quick86ItemHandler)
            .WithName("Quick86Item")
            .RequireAuthorization("Permission:menu.inventory.quick86");

        group.MapPost("/menus/{menuId:guid}/items/{itemId:guid}/restock", RestockItemHandler)
            .WithName("RestockItem")
            .RequireAuthorization("Permission:menu.inventory.quick86");

        group.MapPost("/menus/{menuId:guid}/items/{itemId:guid}/variants/{variantId:guid}/quick-86", Quick86VariantHandler)
            .WithName("Quick86Variant")
            .RequireAuthorization("Permission:menu.inventory.quick86");

        group.MapPost("/menus/{menuId:guid}/items/{itemId:guid}/variants/{variantId:guid}/restock", RestockVariantHandler)
            .WithName("RestockVariant")
            .RequireAuthorization("Permission:menu.inventory.quick86");
    }

    internal static async Task<IResult> ListBranchAvailabilitiesHandler(
        Guid branchId,
        HttpContext context,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        ICatalogService catalogService,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(context, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var list = await catalogService.ListBranchAvailabilitiesAsync(
                tenantId,
                new BranchId(branchId),
                actor,
                ct);

            return Results.Ok(list);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }

    internal static async Task<IResult> Quick86ItemHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        Quick86ItemCommand command,
        HttpContext context,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        ICatalogService catalogService,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(context, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(command.ConcurrencyToken, context.Request);
            var finalCommand = command with { ConcurrencyToken = token };

            var result = await catalogService.Quick86ItemAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                finalCommand,
                actor,
                ct);

            return AvailabilityResult(context, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }

    internal static async Task<IResult> RestockItemHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        RestockItemCommand command,
        HttpContext context,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        ICatalogService catalogService,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(context, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(command.ConcurrencyToken, context.Request);
            var finalCommand = command with { ConcurrencyToken = token };

            var result = await catalogService.RestockItemAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                finalCommand,
                actor,
                ct);

            return AvailabilityResult(context, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }

    internal static async Task<IResult> Quick86VariantHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        Guid variantId,
        Quick86VariantCommand command,
        HttpContext context,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        ICatalogService catalogService,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(context, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(command.ConcurrencyToken, context.Request);
            var finalCommand = command with { ConcurrencyToken = token };

            var result = await catalogService.Quick86VariantAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                new ItemVariantId(variantId),
                finalCommand,
                actor,
                ct);

            return AvailabilityResult(context, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }

    internal static async Task<IResult> RestockVariantHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        Guid variantId,
        RestockVariantCommand command,
        HttpContext context,
        ITenantContext tenantContext,
        IJwtClaimPrincipalParser parser,
        ICatalogService catalogService,
        CancellationToken ct)
    {
        try
        {
            var actor = GetActor(context, parser);
            var tenantId = ResolveTenantId(tenantContext, actor);
            var token = ExtractConcurrencyToken(command.ConcurrencyToken, context.Request);
            var finalCommand = command with { ConcurrencyToken = token };

            var result = await catalogService.RestockVariantAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                new ItemVariantId(variantId),
                finalCommand,
                actor,
                ct);

            return AvailabilityResult(context, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }
}
