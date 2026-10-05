using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;

namespace RestaurantOrder.Api.Catalog;

public static partial class CatalogEndpoints
{
    private static void MapRuntimeMenuEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/runtime-menu", GetRuntimeMenuHandler)
            .WithName("GetRuntimeMenu")
            .RequireAuthorization("Permission:menu.catalog.view");
    }

    internal static async Task<IResult> GetRuntimeMenuHandler(
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
            var runtimeMenu = await catalogService.GetRuntimeMenuAsync(
                tenantId,
                new BranchId(branchId),
                actor,
                ct);

            return Results.Ok(runtimeMenu);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }
}
