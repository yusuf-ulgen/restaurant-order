using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.FeatureFlags;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Api.RestaurantConfig;

public sealed record UpdateFeatureFlagsApiRequest(
    Dictionary<string, bool> Flags,
    Guid? ConcurrencyToken = null);

public sealed record ClearBranchFeatureOverrideApiRequest(
    Guid? ConcurrencyToken = null);

public static partial class RestaurantConfigEndpoints
{
    internal static IResult? ValidateFeatureFlagsPayload(Dictionary<string, bool>? flags)
    {
        if (flags == null)
        {
            return null;
        }

        try
        {
            foreach (var key in flags.Keys)
            {
                FeatureFlagKey.ValidateKey(key);
            }
            return null;
        }
        catch (DomainException ex)
        {
            return HandleException(ex);
        }
    }

    private static void MapFeatureFlagsEndpoints(RouteGroupBuilder root)
    {
        // ==========================================
        // Tenant Feature Flag Defaults
        // ==========================================

        var tenantFeaturesGroup = root.MapGroup("/tenant/features");

        tenantFeaturesGroup.MapGet("/", async (
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var result = await service.GetTenantFeatureFlagsAsync(tenantId, actor, ct);

                if (result.ConcurrencyToken != Guid.Empty)
                {
                    httpContext.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
                }
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandingView)
        .WithName("GetTenantFeatureFlags");

        tenantFeaturesGroup.MapPut("/", async (
            UpdateFeatureFlagsApiRequest request,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            if (ValidateFeatureFlagsPayload(request.Flags) is { } validationError)
            {
                return validationError;
            }

            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for updates.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var command = new UpdateFeatureFlagsCommand(request.Flags, token);
                var result = await service.UpdateTenantFeatureFlagsAsync(tenantId, command, actor, ct);

                httpContext.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBrandsManage)
        .WithName("UpdateTenantFeatureFlags");

        // ==========================================
        // Branch Feature Flag Overrides
        // ==========================================

        var branchFeaturesGroup = root.MapGroup("/branches/{branchId:guid}/features");

        branchFeaturesGroup.MapGet("/override", async (
            Guid branchId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var result = await service.GetBranchFeatureFlagsOverrideAsync(tenantId, new BranchId(branchId), actor, ct);

                if (result.ConcurrencyToken != Guid.Empty)
                {
                    httpContext.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
                }
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationView)
        .WithName("GetBranchFeatureFlagsOverride");

        branchFeaturesGroup.MapPut("/override", async (
            Guid branchId,
            UpdateFeatureFlagsApiRequest request,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            if (ValidateFeatureFlagsPayload(request.Flags) is { } validationError)
            {
                return validationError;
            }

            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for updates.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var command = new UpdateFeatureFlagsCommand(request.Flags, token);
                var result = await service.UpdateBranchFeatureFlagsOverrideAsync(tenantId, new BranchId(branchId), command, actor, ct);

                httpContext.Response.Headers.ETag = $"\"{result.ConcurrencyToken:D}\"";
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("UpdateBranchFeatureFlagsOverride");

        branchFeaturesGroup.MapDelete("/override", async (
            Guid branchId,
            HttpContext httpContext,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            Guid? bodyToken = null;
            if (httpContext.Request.HasJsonContentType())
            {
                try
                {
                    var req = await httpContext.Request.ReadFromJsonAsync<ClearBranchFeatureOverrideApiRequest>(cancellationToken: ct);
                    bodyToken = req?.ConcurrencyToken;
                }
                catch { }
            }

            var token = ExtractConcurrencyToken(bodyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var result = await service.ClearBranchFeatureFlagsOverrideAsync(tenantId, new BranchId(branchId), token.Value, actor, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("ClearBranchFeatureFlagsOverride");

        branchFeaturesGroup.MapGet("/effective", async (
            Guid branchId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var result = await service.GetEffectiveFeatureFlagsAsync(tenantId, new BranchId(branchId), actor, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationView)
        .WithName("GetEffectiveFeatureFlags");
    }
}
