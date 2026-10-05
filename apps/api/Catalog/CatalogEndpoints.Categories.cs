using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;

namespace RestaurantOrder.Api.Catalog;

public sealed record CreateMenuCategoryApiRequest(
    string Name,
    string Slug,
    string? Description = null,
    int SortOrder = 0);

public sealed record UpdateMenuCategoryApiRequest(
    string Name,
    string? Description = null,
    int SortOrder = 0,
    Guid? ConcurrencyToken = null);

public sealed record MenuCategoryStateApiRequest(
    Guid? ConcurrencyToken = null);

public sealed record CategoryReorderItemApiRequest(
    Guid Id,
    int SortOrder,
    Guid? ConcurrencyToken = null);

public sealed record ReorderCategoriesApiRequest(
    IReadOnlyList<CategoryReorderItemApiRequest> Items);

public static partial class CatalogEndpoints
{
    private static void MapCategoryEndpoints(RouteGroupBuilder branchGroup)
    {
        var categoryGroup = branchGroup.MapGroup("/menus/{menuId:guid}/categories");

        categoryGroup.MapGet("", ListCategoriesHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("ListCategories");

        categoryGroup.MapGet("/{categoryId:guid}", GetCategoryHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogView)
            .WithName("GetCategory");

        categoryGroup.MapPost("", CreateCategoryHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("CreateCategory");

        categoryGroup.MapPut("/{categoryId:guid}", UpdateCategoryHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("UpdateCategory");

        categoryGroup.MapPost("/{categoryId:guid}/activate", ActivateCategoryHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ActivateCategory");

        categoryGroup.MapPost("/{categoryId:guid}/deactivate", DeactivateCategoryHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("DeactivateCategory");

        categoryGroup.MapPost("/reorder", ReorderCategoriesHandler)
            .RequireAuthorization()
            .RequirePermission(Permissions.MenuCatalogManage)
            .WithName("ReorderCategories");
    }

    internal static async Task<IResult> ListCategoriesHandler(
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
            var result = await service.ListCategoriesAsync(tenantId, new BranchId(branchId), new MenuId(menuId), actor, ct);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> GetCategoryHandler(
        Guid branchId,
        Guid menuId,
        Guid categoryId,
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
            var result = await service.GetCategoryAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuCategoryId(categoryId),
                actor,
                ct);
            return CategoryResult(httpContext, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> CreateCategoryHandler(
        Guid branchId,
        Guid menuId,
        CreateMenuCategoryApiRequest request,
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
            var command = new CreateMenuCategoryCommand(
                Name: request.Name,
                Slug: request.Slug,
                Description: request.Description,
                SortOrder: request.SortOrder);

            var result = await service.CreateCategoryAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                command,
                actor,
                ct);

            return CategoryResult(httpContext, result, StatusCodes.Status201Created);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> UpdateCategoryHandler(
        Guid branchId,
        Guid menuId,
        Guid categoryId,
        UpdateMenuCategoryApiRequest request,
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

            var command = new UpdateMenuCategoryCommand(
                Name: request.Name,
                Description: request.Description,
                SortOrder: request.SortOrder,
                ConcurrencyToken: token);

            var result = await service.UpdateCategoryAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuCategoryId(categoryId),
                command,
                actor,
                ct);

            return CategoryResult(httpContext, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ActivateCategoryHandler(
        Guid branchId,
        Guid menuId,
        Guid categoryId,
        MenuCategoryStateApiRequest? request,
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

            var result = await service.ActivateCategoryAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuCategoryId(categoryId),
                token,
                actor,
                ct);

            return CategoryResult(httpContext, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> DeactivateCategoryHandler(
        Guid branchId,
        Guid menuId,
        Guid categoryId,
        MenuCategoryStateApiRequest? request,
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

            var result = await service.DeactivateCategoryAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                new MenuCategoryId(categoryId),
                token,
                actor,
                ct);

            return CategoryResult(httpContext, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }

    internal static async Task<IResult> ReorderCategoriesHandler(
        Guid branchId,
        Guid menuId,
        ReorderCategoriesApiRequest request,
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

            if (request.Items == null || request.Items.Count == 0)
            {
                throw new RestaurantOrder.Domain.Common.DomainException("Reorder items cannot be empty.");
            }

            var items = new List<CategoryReorderItem>();
            foreach (var item in request.Items)
            {
                if (!item.ConcurrencyToken.HasValue || item.ConcurrencyToken.Value == Guid.Empty)
                {
                    throw new ConcurrencyPreconditionException(
                        $"Concurrency token is required for category '{item.Id}' in reorder list.");
                }

                items.Add(new CategoryReorderItem(item.Id, item.SortOrder, item.ConcurrencyToken.Value));
            }

            var command = new ReorderCategoriesCommand(items);
            var result = await service.ReorderCategoriesAsync(
                tenantId,
                new BranchId(branchId),
                new MenuId(menuId),
                command,
                actor,
                ct);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, httpContext);
        }
    }
}
