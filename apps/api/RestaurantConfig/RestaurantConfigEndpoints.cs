using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Tenancy;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

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
        MapDiningAreasEndpoints(root);
        MapPreparationStationsEndpoints(root);
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

    internal static Guid? ExtractConcurrencyToken(Guid? bodyToken, HttpRequest request)
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

    internal static IResult BrandResult(HttpContext context, BrandDto brand, int statusCode = StatusCodes.Status200OK)
    {
        context.Response.Headers.ETag = $"\"{brand.ConcurrencyToken:D}\"";
        return statusCode == StatusCodes.Status201Created
            ? Results.Created($"/api/v1/restaurant-config/brands/{brand.Id}", brand)
            : Results.Ok(brand);
    }

    internal static IResult BranchResult(HttpContext context, BranchDto branch, int statusCode = StatusCodes.Status200OK)
    {
        context.Response.Headers.ETag = $"\"{branch.ConcurrencyToken:D}\"";
        return statusCode == StatusCodes.Status201Created
            ? Results.Created($"/api/v1/restaurant-config/branches/{branch.Id}", branch)
            : Results.Ok(branch);
    }

    internal static TenantId ResolveTenantId(ITenantContext tenantContext, AuthenticatedPrincipal actor)
    {
        if (tenantContext.HasTenant && tenantContext.TenantId.HasValue && tenantContext.TenantId.Value != Guid.Empty)
        {
            return new TenantId(tenantContext.TenantId.Value);
        }

        if (actor.Scope.TenantId.HasValue && actor.Scope.TenantId.Value.Value != Guid.Empty)
        {
            return actor.Scope.TenantId.Value;
        }

        throw new InvalidAuthorizationScopeException("Tenant context is required but missing or unauthenticated.");
    }

    internal static string ResolveCorrelationId(HttpContext? context)
    {
        if (context != null)
        {
            var header = context.Response?.Headers["X-Correlation-Id"].ToString();
            if (!string.IsNullOrWhiteSpace(header))
            {
                return header;
            }

            header = context.Request?.Headers["X-Correlation-Id"].ToString();
            if (!string.IsNullOrWhiteSpace(header))
            {
                context.Response?.Headers.TryAdd("X-Correlation-Id", header);
                return header;
            }

            if (!string.IsNullOrWhiteSpace(context.TraceIdentifier))
            {
                context.Response?.Headers.TryAdd("X-Correlation-Id", context.TraceIdentifier);
                return context.TraceIdentifier;
            }
        }

        return Guid.NewGuid().ToString("D");
    }

    internal static IResult HandleException(Exception ex, HttpContext? context = null)
    {
        var correlationId = ResolveCorrelationId(context);
        var logger = context?.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger("RestaurantOrder.Api.RestaurantConfig");

        if (ex is ResourceNotFoundException or DuplicateCodeException or DuplicateSlugException
            or ConcurrencyConflictException or ConcurrencyPreconditionException or InvalidAuthorizationScopeException
            or DomainException or ArgumentException)
        {
            logger?.LogInformation(
                "Handled domain error {ExceptionType}: {Message}. CorrelationId: {CorrelationId}",
                ex.GetType().Name,
                ex.Message,
                correlationId);
        }
        else
        {
            logger?.LogError(
                ex,
                "Unhandled error in restaurant configuration endpoints. CorrelationId: {CorrelationId}",
                correlationId);
        }

        var extensions = new Dictionary<string, object?>
        {
            ["correlationId"] = correlationId
        };

        return ex switch
        {
            ResourceNotFoundException rnfe => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: rnfe.Message, type: "https://httpstatuses.com/404", extensions: extensions),
            DuplicateCodeException dce => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: dce.Message, type: "https://httpstatuses.com/409", extensions: extensions),
            DuplicateSlugException dse => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: dse.Message, type: "https://httpstatuses.com/409", extensions: extensions),
            ConcurrencyConflictException cce => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: cce.Message, type: "https://httpstatuses.com/409", extensions: extensions),
            ConcurrencyPreconditionException cpe => Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: cpe.Message, type: "https://httpstatuses.com/412", extensions: extensions),
            InvalidAuthorizationScopeException iase => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: iase.Message, type: "https://httpstatuses.com/403", extensions: extensions),
            DomainException de => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: de.Message, type: "https://httpstatuses.com/400", extensions: extensions),
            ArgumentException ae => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ae.Message, type: "https://httpstatuses.com/400", extensions: extensions),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error", detail: "An unexpected error occurred while processing your request.", type: "https://httpstatuses.com/500", extensions: extensions)
        };
    }
}
