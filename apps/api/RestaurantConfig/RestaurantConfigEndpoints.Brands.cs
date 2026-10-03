using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Api.RestaurantConfig;

public static partial class RestaurantConfigEndpoints
{
    private static void MapBrandEndpoints(RouteGroupBuilder root)
    {
        var group = root.MapGroup("/brands");

        group.MapGet("/", async (
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var tenantId = new TenantId(tenantContext.TenantId!.Value);
            var brands = await service.ListBrandsAsync(tenantId, ct);
            return Results.Ok(brands);
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandsManage)
        .WithName("ListBrands")
        .WithSummary("List all brands for current tenant");

        group.MapGet("/{brandId:guid}", async (
            Guid brandId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var tenantId = new TenantId(tenantContext.TenantId!.Value);
            var brand = await service.GetBrandByIdAsync(tenantId, new BrandId(brandId), ct);
            if (brand == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: $"Brand '{brandId}' not found.", type: "https://httpstatuses.com/404");
            }
            return BrandResult(httpContext, brand);
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandsManage)
        .WithName("GetBrand")
        .WithSummary("Get brand details by ID");

        group.MapPost("/", async (
            CreateBrandApiRequest request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new CreateBrandCommand(request.Name, request.Slug);
                var brand = await service.CreateBrandAsync(tenantId, command, actor, ct);
                return BrandResult(httpContext, brand, StatusCodes.Status201Created);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandsManage)
        .WithName("CreateBrand")
        .WithSummary("Create a new brand in tenant");

        group.MapPut("/{brandId:guid}", async (
            Guid brandId,
            UpdateBrandApiRequest request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for brand updates.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new UpdateBrandCommand(request.Name, token);
                var brand = await service.UpdateBrandAsync(tenantId, new BrandId(brandId), command, actor, ct);
                return BrandResult(httpContext, brand);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandsManage)
        .WithName("UpdateBrand")
        .WithSummary("Update brand name with concurrency control");

        group.MapPost("/{brandId:guid}/activate", async (
            Guid brandId,
            BrandStateApiRequest? request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for brand activation.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new BrandStateChangeCommand(token);
                var brand = await service.ActivateBrandAsync(tenantId, new BrandId(brandId), command, actor, ct);
                return BrandResult(httpContext, brand);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandsManage)
        .WithName("ActivateBrand")
        .WithSummary("Activate an inactive brand");

        group.MapPost("/{brandId:guid}/deactivate", async (
            Guid brandId,
            BrandStateApiRequest? request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for brand deactivation.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new BrandStateChangeCommand(token);
                var brand = await service.DeactivateBrandAsync(tenantId, new BrandId(brandId), command, actor, ct);
                return BrandResult(httpContext, brand);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandsManage)
        .WithName("DeactivateBrand")
        .WithSummary("Deactivate an active brand");
    }
}
