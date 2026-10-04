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

public sealed record CreateItemVariantApiRequest(
    string Name,
    string Code,
    long AbsolutePriceMinorUnits,
    int SortOrder = 0,
    bool IsDefault = false);

public sealed record UpdateItemVariantApiRequest(
    string Name,
    string Code,
    int SortOrder = 0,
    bool IsDefault = false,
    long? AbsolutePriceMinorUnits = null,
    Guid? ConcurrencyToken = null);

public sealed record UpdateItemVariantPriceApiRequest(
    long AbsolutePriceMinorUnits,
    Guid? ConcurrencyToken = null);

public sealed record ItemVariantStateApiRequest(
    Guid? ConcurrencyToken = null);

public sealed record VariantReorderItemApiRequest(
    Guid Id,
    int SortOrder,
    Guid? ConcurrencyToken = null);

public sealed record ReorderItemVariantsApiRequest(
    IReadOnlyList<VariantReorderItemApiRequest> Items);

public static partial class CatalogEndpoints
{
    private static void MapVariantEndpoints(RouteGroupBuilder branchGroup)
    {
        var variantGroup = branchGroup.MapGroup("/menus/{menuId:guid}/items/{itemId:guid}/variants");

        variantGroup.MapGet("", ListVariantsHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("ListItemVariants");

        variantGroup.MapGet("/{variantId:guid}", GetVariantHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("GetItemVariant");

        variantGroup.MapPost("", CreateVariantHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("CreateItemVariant");

        variantGroup.MapPut("/{variantId:guid}", UpdateVariantHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("UpdateItemVariant");

        variantGroup.MapPut("/{variantId:guid}/price", UpdateVariantPriceHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuPricingManage)
            .WithName("UpdateItemVariantPrice");

        variantGroup.MapPost("/{variantId:guid}/activate", ActivateVariantHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ActivateItemVariant");

        variantGroup.MapPost("/{variantId:guid}/deactivate", DeactivateVariantHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("DeactivateItemVariant");

        variantGroup.MapPost("/reorder", ReorderVariantsHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ReorderItemVariants");
    }

    internal static async Task<IResult> ListVariantsHandler(
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

            var variants = await catalogService.ListVariantsAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                actor,
                cancellationToken);

            return Results.Ok(variants);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> GetVariantHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        Guid variantId,
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

            var variant = await catalogService.GetVariantAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                new ItemVariantId(variantId),
                actor,
                cancellationToken);

            return VariantResult(httpContext, variant);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> CreateVariantHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        CreateItemVariantApiRequest request,
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

            var command = new CreateItemVariantCommand(
                Name: request.Name,
                Code: request.Code,
                AbsolutePriceMinorUnits: request.AbsolutePriceMinorUnits,
                SortOrder: request.SortOrder,
                IsDefault: request.IsDefault);

            var created = await catalogService.CreateVariantAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                command,
                actor,
                cancellationToken);

            return VariantResult(httpContext, created, StatusCodes.Status201Created);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> UpdateVariantHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        Guid variantId,
        UpdateItemVariantApiRequest request,
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

            var command = new UpdateItemVariantCommand(
                Name: request.Name,
                Code: request.Code,
                SortOrder: request.SortOrder,
                IsDefault: request.IsDefault,
                AbsolutePriceMinorUnits: request.AbsolutePriceMinorUnits,
                ConcurrencyToken: token);

            var updated = await catalogService.UpdateVariantAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                new ItemVariantId(variantId),
                command,
                actor,
                cancellationToken);

            return VariantResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> UpdateVariantPriceHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        Guid variantId,
        UpdateItemVariantPriceApiRequest request,
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

            var command = new UpdateItemVariantPriceCommand(
                AbsolutePriceMinorUnits: request.AbsolutePriceMinorUnits,
                ConcurrencyToken: token);

            var updated = await catalogService.UpdateVariantPriceAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                new ItemVariantId(variantId),
                command,
                actor,
                cancellationToken);

            return VariantResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ActivateVariantHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        Guid variantId,
        ItemVariantStateApiRequest? request,
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

            var updated = await catalogService.ActivateVariantAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                new ItemVariantId(variantId),
                token,
                actor,
                cancellationToken);

            return VariantResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> DeactivateVariantHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        Guid variantId,
        ItemVariantStateApiRequest? request,
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

            var updated = await catalogService.DeactivateVariantAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
                new ItemVariantId(variantId),
                token,
                actor,
                cancellationToken);

            return VariantResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ReorderVariantsHandler(
        Guid branchId,
        Guid menuId,
        Guid itemId,
        ReorderItemVariantsApiRequest request,
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

            var items = new List<VariantReorderItem>();
            foreach (var item in request.Items)
            {
                if (!item.ConcurrencyToken.HasValue || item.ConcurrencyToken.Value == Guid.Empty)
                {
                    throw new ConcurrencyPreconditionException(
                        $"Concurrency token is required for variant '{item.Id}' in reorder list.");
                }

                items.Add(new VariantReorderItem(item.Id, item.SortOrder, item.ConcurrencyToken.Value));
            }

            var command = new ReorderItemVariantsCommand(items);
            var result = await catalogService.ReorderVariantsAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuItemId(itemId),
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
