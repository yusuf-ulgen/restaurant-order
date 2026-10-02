using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Api.RestaurantConfig;

public sealed record UpdateBrandThemeApiRequest(
    string DisplayName,
    string PrimaryColor,
    string PrimaryHoverColor,
    string SecondaryColor,
    string AccentColor,
    string SurfaceColor,
    string BackgroundColor,
    string? LogoUrl = null,
    string? FaviconUrl = null,
    string? FooterText = null,
    string? DefaultShellTitle = null,
    string? DefaultShellSubtitle = null,
    Guid? ConcurrencyToken = null);

public sealed record UpdateBranchThemeOverrideApiRequest(
    string? DisplayName = null,
    string? LogoUrl = null,
    string? HeaderSubtitle = null,
    string? FooterBranchInfo = null,
    Guid? ConcurrencyToken = null);

public static partial class RestaurantConfigEndpoints
{
    private static void MapBrandingEndpoints(RouteGroupBuilder root)
    {
        // 1. Brand Theme Endpoints: /api/v1/restaurant-config/brands/{brandId}/theme
        var brandThemeGroup = root.MapGroup("/brands/{brandId:guid}/theme");

        brandThemeGroup.MapGet("/", async (
            Guid brandId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var tenantId = new TenantId(tenantContext.TenantId!.Value);
            var theme = await service.GetBrandThemeAsync(tenantId, new BrandId(brandId), ct);
            if (theme == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: $"Brand '{brandId}' not found.", type: "https://httpstatuses.com/404");
            }

            if (theme.ConcurrencyToken != Guid.Empty)
            {
                httpContext.Response.Headers.ETag = $"\"{theme.ConcurrencyToken:D}\"";
            }
            return Results.Ok(theme);
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandingView)
        .WithName("GetBrandTheme")
        .WithSummary("Get brand theme and appearance settings");

        brandThemeGroup.MapPut("/", async (
            Guid brandId,
            UpdateBrandThemeApiRequest request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for brand theme updates.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);
                var command = new UpdateBrandThemeCommand(
                    DisplayName: request.DisplayName,
                    LogoUrl: request.LogoUrl,
                    FaviconUrl: request.FaviconUrl,
                    PrimaryColor: request.PrimaryColor,
                    PrimaryHoverColor: request.PrimaryHoverColor,
                    SecondaryColor: request.SecondaryColor,
                    AccentColor: request.AccentColor,
                    SurfaceColor: request.SurfaceColor,
                    BackgroundColor: request.BackgroundColor,
                    FooterText: request.FooterText,
                    DefaultShellTitle: request.DefaultShellTitle,
                    DefaultShellSubtitle: request.DefaultShellSubtitle,
                    ConcurrencyToken: token);

                var theme = await service.UpdateBrandThemeAsync(tenantId, new BrandId(brandId), command, actor, ct);
                httpContext.Response.Headers.ETag = $"\"{theme.ConcurrencyToken:D}\"";
                return Results.Ok(theme);
            }
            catch (ResourceNotFoundException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: ex.Message, type: "https://httpstatuses.com/404");
            }
            catch (ConcurrencyConflictException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: ex.Message, type: "https://httpstatuses.com/409");
            }
            catch (InvalidAuthorizationScopeException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message, type: "https://httpstatuses.com/403");
            }
            catch (DomainException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ex.Message, type: "https://httpstatuses.com/400");
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandingManage)
        .WithName("UpdateBrandTheme")
        .WithSummary("Update brand appearance settings");

        // 2. Branch Theme Endpoints: /api/v1/restaurant-config/branches/{branchId}/theme
        var branchThemeGroup = root.MapGroup("/branches/{branchId:guid}/theme");

        branchThemeGroup.MapGet("/", async (
            Guid branchId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var tenantId = new TenantId(tenantContext.TenantId!.Value);
            var theme = await service.GetEffectiveBranchThemeAsync(tenantId, new BranchId(branchId), ct);
            if (theme == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: $"Branch '{branchId}' not found.", type: "https://httpstatuses.com/404");
            }

            return Results.Ok(theme);
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandingView)
        .WithName("GetEffectiveBranchTheme")
        .WithSummary("Get effective theme for a branch with brand inheritance");

        branchThemeGroup.MapPut("/", async (
            Guid branchId,
            UpdateBranchThemeOverrideApiRequest request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for branch theme override updates.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = new TenantId(tenantContext.TenantId!.Value);

                // BranchManager can only manage their own branch
                if (actor.Role == AuthRole.BranchManager && actor.BranchId != new BranchId(branchId))
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "BranchManager cannot update theme overrides for another branch.", type: "https://httpstatuses.com/403");
                }

                var command = new UpdateBranchThemeOverrideCommand(
                    DisplayName: request.DisplayName,
                    LogoUrl: request.LogoUrl,
                    HeaderSubtitle: request.HeaderSubtitle,
                    FooterBranchInfo: request.FooterBranchInfo,
                    ConcurrencyToken: token);

                var overrideDto = await service.UpdateBranchThemeOverrideAsync(tenantId, new BranchId(branchId), command, actor, ct);
                httpContext.Response.Headers.ETag = $"\"{overrideDto.ConcurrencyToken:D}\"";
                return Results.Ok(overrideDto);
            }
            catch (ResourceNotFoundException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: ex.Message, type: "https://httpstatuses.com/404");
            }
            catch (ConcurrencyConflictException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: ex.Message, type: "https://httpstatuses.com/409");
            }
            catch (InvalidAuthorizationScopeException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message, type: "https://httpstatuses.com/403");
            }
            catch (DomainException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ex.Message, type: "https://httpstatuses.com/400");
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandingManage)
        .WithName("UpdateBranchThemeOverride")
        .WithSummary("Update branch theme override settings");

        branchThemeGroup.MapDelete("/", async (
            Guid branchId,
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

                // BranchManager can only manage their own branch
                if (actor.Role == AuthRole.BranchManager && actor.BranchId != new BranchId(branchId))
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "BranchManager cannot clear theme overrides for another branch.", type: "https://httpstatuses.com/403");
                }

                var effectiveTheme = await service.ClearBranchThemeOverrideAsync(tenantId, new BranchId(branchId), actor, ct);
                return Results.Ok(effectiveTheme);
            }
            catch (ResourceNotFoundException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: ex.Message, type: "https://httpstatuses.com/404");
            }
            catch (InvalidAuthorizationScopeException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message, type: "https://httpstatuses.com/403");
            }
            catch (DomainException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ex.Message, type: "https://httpstatuses.com/400");
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandingManage)
        .WithName("ClearBranchThemeOverride")
        .WithSummary("Clear branch theme overrides and revert to inherited brand theme");
    }
}
