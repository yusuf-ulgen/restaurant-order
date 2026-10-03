using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Tenancy;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Api.RestaurantConfig;

public sealed record CreateBrandApiRequest(string Name, string Slug);
public sealed record UpdateBrandApiRequest(string Name, Guid? ConcurrencyToken = null);
public sealed record BrandStateApiRequest(Guid? ConcurrencyToken = null);

public sealed record CreateBranchApiRequest(
    Guid BrandId,
    string Name,
    string Slug,
    string? Timezone = null,
    string? Currency = null);

public sealed record UpdateBranchApiRequest(
    string Name,
    string Timezone,
    string Currency,
    Guid? ConcurrencyToken = null);

public sealed record BranchStateApiRequest(
    string? Reason = null,
    Guid? ConcurrencyToken = null);

public static partial class RestaurantConfigEndpoints
{
    public static IEndpointRouteBuilder MapRestaurantConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var root = app.MapGroup("/api/v1/restaurant-config")
            .WithTags("Restaurant Configuration");

        MapBrandEndpoints(root);
        MapBranchEndpoints(root);
        MapBrandingEndpoints(root);
        MapBranchSettingsEndpoints(root);
        MapDiningAreasAndStationsEndpoints(root);
        MapFeatureFlagsEndpoints(root);

        return app;
    }

    private static AuthenticatedPrincipal GetActor(HttpContext httpContext, IJwtClaimPrincipalParser parser)
    {
        var claimsDict = httpContext.User.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        return parser.ParsePrincipal(claimsDict);
    }

    private static Guid? ExtractConcurrencyToken(Guid? bodyToken, HttpRequest request)
    {
        if (bodyToken.HasValue && bodyToken.Value != Guid.Empty)
        {
            return bodyToken.Value;
        }

        var ifMatch = request.Headers.IfMatch.ToString();
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            var raw = ifMatch.Trim().Trim('"');
            if (Guid.TryParse(raw, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static IResult BrandResult(HttpContext context, BrandDto brand, int statusCode = StatusCodes.Status200OK)
    {
        context.Response.Headers.ETag = $"\"{brand.ConcurrencyToken:D}\"";
        return statusCode == StatusCodes.Status201Created
            ? Results.Created($"/api/v1/restaurant-config/brands/{brand.Id}", brand)
            : Results.Ok(brand);
    }

    private static IResult BranchResult(HttpContext context, BranchDto branch, int statusCode = StatusCodes.Status200OK)
    {
        context.Response.Headers.ETag = $"\"{branch.ConcurrencyToken:D}\"";
        return statusCode == StatusCodes.Status201Created
            ? Results.Created($"/api/v1/restaurant-config/branches/{branch.Id}", branch)
            : Results.Ok(branch);
    }
}
