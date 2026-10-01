using System.Security.Cryptography;
using System.Text;

namespace RestaurantOrder.Api.Auth;

/// <summary>
/// Defense-in-depth CSRF validation middleware for cookie-authenticated mutation requests.
/// Enforces double-submit cookie verification on all state-changing HTTP methods.
/// </summary>
public sealed class CsrfValidationMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly HashSet<string> MutationMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Delete,
        HttpMethods.Patch
    };

    public CsrfValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip non-mutation methods
        if (!MutationMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Exempt endpoints that do not have an ambient session established
        // Documented exemptions:
        // - /api/v1/auth/login: Initial email/password pre-authentication (no ambient cookie yet)
        // - /api/v1/terminals/activate: Device enrollment pairing (no terminal credentials yet)
        // - /api/v1/auth/pin/login: Initial terminal staff PIN login (no staff auth cookie yet)
        // - /health/: Infrastructure probes
        // - /api/v1/dev/: Local synthetic seed endpoints
        if (path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/v1/terminals/activate", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/v1/auth/pin/login", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/dev/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Enforce CSRF if client has any ambient authentication/session cookies or supplies a CSRF header
        var hasAuthCookie = context.Request.Cookies.ContainsKey(AuthCookieService.AccessTokenCookieName) ||
                            context.Request.Cookies.ContainsKey(AuthCookieService.RefreshTokenCookieName) ||
                            context.Request.Cookies.ContainsKey(AuthCookieService.CsrfTokenCookieName);
        var hasCsrfHeader = context.Request.Headers.ContainsKey(AuthCookieService.CsrfHeaderName);

        if (hasAuthCookie || hasCsrfHeader)
        {
            var cookieCsrf = context.Request.Cookies[AuthCookieService.CsrfTokenCookieName];
            var headerCsrf = context.Request.Headers[AuthCookieService.CsrfHeaderName].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(cookieCsrf) ||
                string.IsNullOrWhiteSpace(headerCsrf) ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(cookieCsrf.Trim()),
                    Encoding.UTF8.GetBytes(headerCsrf.Trim())))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                var problemJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "https://httpstatuses.com/403",
                    title = "Forbidden",
                    status = 403,
                    detail = "CSRF token missing or invalid."
                });
                await context.Response.WriteAsync(problemJson);
                return;
            }
        }

        await _next(context);
    }
}
