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
    private static void MapModifierEndpoints(RouteGroupBuilder branchGroup)
    {
        var groupRoute = branchGroup.MapGroup("/modifier-groups");

        groupRoute.MapGet("", ListModifierGroupsHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("ListModifierGroups");

        groupRoute.MapGet("/{groupId:guid}", GetModifierGroupHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("GetModifierGroup");

        groupRoute.MapPost("", CreateModifierGroupHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("CreateModifierGroup");

        groupRoute.MapPut("/{groupId:guid}", UpdateModifierGroupHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("UpdateModifierGroup");

        groupRoute.MapPost("/{groupId:guid}/activate", ActivateModifierGroupHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ActivateModifierGroup");

        groupRoute.MapPost("/{groupId:guid}/deactivate", DeactivateModifierGroupHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("DeactivateModifierGroup");

        MapModifierOptionEndpoints(groupRoute);
    }

    internal static async Task<IResult> ListModifierGroupsHandler(
        Guid branchId,
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

            var groups = await catalogService.ListModifierGroupsAsync(
                tenantId,
                new BranchId(branchId),
                actor,
                cancellationToken);

            return Results.Ok(groups);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> GetModifierGroupHandler(
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

            var group = await catalogService.GetModifierGroupAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                actor,
                cancellationToken);

            return ModifierGroupResult(httpContext, group);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> CreateModifierGroupHandler(
        Guid branchId,
        CreateModifierGroupCommand command,
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

            var group = await catalogService.CreateModifierGroupAsync(
                tenantId,
                new BranchId(branchId),
                command,
                actor,
                cancellationToken);

            return ModifierGroupResult(httpContext, group, StatusCodes.Status201Created);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> UpdateModifierGroupHandler(
        Guid branchId,
        Guid groupId,
        UpdateModifierGroupCommand command,
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

            var updated = await catalogService.UpdateModifierGroupAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                command with { ConcurrencyToken = token },
                actor,
                cancellationToken);

            return ModifierGroupResult(httpContext, updated);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ActivateModifierGroupHandler(
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
            var token = ExtractConcurrencyToken(null, httpContext.Request);

            var group = await catalogService.ActivateModifierGroupAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                token,
                actor,
                cancellationToken);

            return ModifierGroupResult(httpContext, group);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> DeactivateModifierGroupHandler(
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
            var token = ExtractConcurrencyToken(null, httpContext.Request);

            var group = await catalogService.DeactivateModifierGroupAsync(
                tenantId,
                new BranchId(branchId),
                new ModifierGroupId(groupId),
                token,
                actor,
                cancellationToken);

            return ModifierGroupResult(httpContext, group);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }
}
