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
    private static void MapModifierOptionEndpoints(RouteGroupBuilder groupRoute)
    {
        var optionsRoute = groupRoute.MapGroup("/{groupId:guid}/options");

        optionsRoute.MapGet("", ListModifierOptionsHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("ListModifierOptions");

        optionsRoute.MapGet("/{optionId:guid}", GetModifierOptionHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("GetModifierOption");

        optionsRoute.MapPost("", CreateModifierOptionHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("CreateModifierOption");

        optionsRoute.MapPut("/{optionId:guid}", UpdateModifierOptionHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("UpdateModifierOption");

        optionsRoute.MapPut("/{optionId:guid}/price", UpdateModifierOptionPriceHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuPricingManage)
            .WithName("UpdateModifierOptionPrice");

        optionsRoute.MapPost("/{optionId:guid}/activate", ActivateModifierOptionHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ActivateModifierOption");

        optionsRoute.MapPost("/{optionId:guid}/deactivate", DeactivateModifierOptionHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("DeactivateModifierOption");

        optionsRoute.MapPost("/reorder", ReorderModifierOptionsHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ReorderModifierOptions");
    }

    internal static async Task<IResult> ListModifierOptionsHandler(
        Guid branchId,
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

            var options = await catalogService.ListModifierOptionsAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                actor,
                cancellationToken);

            return Results.Ok(options);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> GetModifierOptionHandler(
        Guid branchId,
        Guid groupId,
        Guid optionId,
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

            var option = await catalogService.GetModifierOptionAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                new ModifierOptionId(optionId),
                actor,
                cancellationToken);

            return ModifierOptionResult(httpContext, option);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> CreateModifierOptionHandler(
        Guid branchId,
        Guid groupId,
        CreateModifierOptionCommand command,
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

            var option = await catalogService.CreateModifierOptionAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                command,
                actor,
                cancellationToken);

            return ModifierOptionResult(httpContext, option, StatusCodes.Status201Created);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> UpdateModifierOptionHandler(
        Guid branchId,
        Guid groupId,
        Guid optionId,
        UpdateModifierOptionCommand command,
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

            var option = await catalogService.UpdateModifierOptionAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                new ModifierOptionId(optionId),
                command with { ConcurrencyToken = token },
                actor,
                cancellationToken);

            return ModifierOptionResult(httpContext, option);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> UpdateModifierOptionPriceHandler(
        Guid branchId,
        Guid groupId,
        Guid optionId,
        UpdateModifierOptionPriceCommand command,
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

            var option = await catalogService.UpdateModifierOptionPriceAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                new ModifierOptionId(optionId),
                command with { ConcurrencyToken = token },
                actor,
                cancellationToken);

            return ModifierOptionResult(httpContext, option);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ActivateModifierOptionHandler(
        Guid branchId,
        Guid groupId,
        Guid optionId,
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

            var option = await catalogService.ActivateModifierOptionAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                new ModifierOptionId(optionId),
                token,
                actor,
                cancellationToken);

            return ModifierOptionResult(httpContext, option);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> DeactivateModifierOptionHandler(
        Guid branchId,
        Guid groupId,
        Guid optionId,
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

            var option = await catalogService.DeactivateModifierOptionAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                new ModifierOptionId(optionId),
                token,
                actor,
                cancellationToken);

            return ModifierOptionResult(httpContext, option);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ReorderModifierOptionsHandler(
        Guid branchId,
        Guid groupId,
        ReorderModifierOptionsCommand command,
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

            var options = await catalogService.ReorderModifierOptionsAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                command,
                actor,
                cancellationToken);

            return Results.Ok(options);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }
}
