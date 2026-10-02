using System.Security.Claims;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Api.Auth;

public sealed record LoginRequest(string Email, string Password, string? TenantSlug = null);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext httpContext,
            IAuthService authService,
            IAuthCookieService cookieService,
            CancellationToken ct) =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();
            var tenantSlug = request.TenantSlug;
            if (string.IsNullOrWhiteSpace(tenantSlug))
            {
                tenantSlug = httpContext.Request.Headers["X-Tenant-Slug"].FirstOrDefault();
            }

            try
            {
                var command = new LoginCommand(request.Email, request.Password, tenantSlug, ip, userAgent);
                var result = await authService.LoginAsync(command, ct);

                var isHttps = httpContext.Request.IsHttps;
                cookieService.SetAuthCookies(
                    httpContext.Response,
                    result.AccessToken,
                    result.RefreshToken,
                    result.AccessTokenExpiresAt,
                    result.RefreshTokenExpiresAt,
                    isHttps);

                return Results.Ok(new
                {
                    user = result.User,
                    session = result.Session,
                    accessTokenExpiresAt = result.AccessTokenExpiresAt,
                    refreshTokenExpiresAt = result.RefreshTokenExpiresAt
                });
            }
            catch (AuthRateLimitException ex)
            {
                httpContext.Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString();
                return Results.Json(new
                {
                    type = "https://httpstatuses.com/429",
                    title = "Too Many Requests",
                    status = 429,
                    detail = ex.Message
                }, statusCode: StatusCodes.Status429TooManyRequests, contentType: "application/problem+json");
            }
            catch (AuthFailureException)
            {
                return Results.Json(new
                {
                    type = "https://httpstatuses.com/401",
                    title = "Unauthorized",
                    status = 401,
                    detail = "Invalid email or password."
                }, statusCode: StatusCodes.Status401Unauthorized, contentType: "application/problem+json");
            }
        })
        .WithName("Login")
        .WithSummary("Authenticate staff or superadmin via email and password");

        group.MapPost("/refresh", async (
            HttpContext httpContext,
            IAuthService authService,
            IAuthCookieService cookieService,
            CancellationToken ct) =>
        {
            var rawRefreshToken = cookieService.GetRefreshToken(httpContext.Request);
            if (string.IsNullOrWhiteSpace(rawRefreshToken))
            {
                return Results.Json(new
                {
                    type = "https://httpstatuses.com/401",
                    title = "Unauthorized",
                    status = 401,
                    detail = "Refresh token missing."
                }, statusCode: StatusCodes.Status401Unauthorized, contentType: "application/problem+json");
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            try
            {
                var result = await authService.RefreshSessionAsync(rawRefreshToken, ip, userAgent, ct);

                var isHttps = httpContext.Request.IsHttps;
                cookieService.SetAuthCookies(
                    httpContext.Response,
                    result.AccessToken,
                    result.RefreshToken,
                    result.AccessTokenExpiresAt,
                    result.RefreshTokenExpiresAt,
                    isHttps);

                return Results.Ok(new
                {
                    user = result.User,
                    session = result.Session,
                    accessTokenExpiresAt = result.AccessTokenExpiresAt,
                    refreshTokenExpiresAt = result.RefreshTokenExpiresAt
                });
            }
            catch (AuthFailureException)
            {
                cookieService.ClearAuthCookies(httpContext.Response, httpContext.Request.IsHttps);
                return Results.Json(new
                {
                    type = "https://httpstatuses.com/401",
                    title = "Unauthorized",
                    status = 401,
                    detail = "Session expired or invalid."
                }, statusCode: StatusCodes.Status401Unauthorized, contentType: "application/problem+json");
            }
        })
        .WithName("Refresh")
        .WithSummary("Rotate refresh token and issue fresh short-lived access token");

        group.MapPost("/logout", async (
            HttpContext httpContext,
            IAuthService authService,
            IAuthCookieService cookieService,
            CancellationToken ct) =>
        {
            var sidStr = httpContext.User.FindFirst(JwtClaimNames.SessionId)?.Value;
            if (Guid.TryParse(sidStr, out var sessionId))
            {
                await authService.LogoutAsync(sessionId, ct);
            }

            cookieService.ClearAuthCookies(httpContext.Response, httpContext.Request.IsHttps);
            return Results.Ok(new { message = "Logged out successfully." });
        })
        .RequireAuthorization()
        .WithName("Logout")
        .WithSummary("Revoke active session and clear authentication cookies");

        group.MapPost("/logout-all", async (
            HttpContext httpContext,
            IAuthService authService,
            IAuthCookieService cookieService,
            CancellationToken ct) =>
        {
            var subStr = httpContext.User.FindFirst(JwtClaimNames.Subject)?.Value;
            if (Guid.TryParse(subStr, out var userIdGuid))
            {
                await authService.LogoutAllAsync(new UserId(userIdGuid), ct);
            }

            cookieService.ClearAuthCookies(httpContext.Response, httpContext.Request.IsHttps);
            return Results.Ok(new { message = "All sessions revoked successfully." });
        })
        .RequireAuthorization()
        .WithName("LogoutAll")
        .WithSummary("Revoke all active sessions and bump security version");

        group.MapGet("/me", (HttpContext httpContext) =>
        {
            var user = httpContext.User;
            var sub = user.FindFirst(JwtClaimNames.Subject)?.Value;
            var role = user.FindFirst(JwtClaimNames.Role)?.Value ?? user.FindFirst(ClaimTypes.Role)?.Value;
            var tenantId = user.FindFirst(JwtClaimNames.TenantId)?.Value;
            var branchId = user.FindFirst(JwtClaimNames.BranchId)?.Value;
            var sid = user.FindFirst(JwtClaimNames.SessionId)?.Value;
            var secVer = user.FindFirst(JwtClaimNames.SecurityVersion)?.Value;

            return Results.Ok(new
            {
                userId = sub,
                role,
                tenantId,
                branchId,
                sessionId = sid,
                securityVersion = secVer
            });
        })
        .RequireAuthorization()
        .WithName("GetCurrentUser")
        .WithSummary("Get authenticated principal metadata");

        group.MapGet("/sessions", async (
            HttpContext httpContext,
            IAuthService authService,
            CancellationToken ct) =>
        {
            var subStr = httpContext.User.FindFirst(JwtClaimNames.Subject)?.Value;
            var sidStr = httpContext.User.FindFirst(JwtClaimNames.SessionId)?.Value;

            if (!Guid.TryParse(subStr, out var userIdGuid))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Invalid token claims.", type: "https://httpstatuses.com/401");
            }

            Guid.TryParse(sidStr, out var currentSessionId);
            var sessions = await authService.GetActiveSessionsAsync(new UserId(userIdGuid), currentSessionId, ct);
            return Results.Ok(sessions);
        })
        .RequireAuthorization()
        .WithName("GetActiveSessions")
        .WithSummary("List all active sessions for current user");

        group.MapDelete("/sessions/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            IAuthService authService,
            IAuthCookieService cookieService,
            CancellationToken ct) =>
        {
            var subStr = httpContext.User.FindFirst(JwtClaimNames.Subject)?.Value;
            var sidStr = httpContext.User.FindFirst(JwtClaimNames.SessionId)?.Value;

            if (!Guid.TryParse(subStr, out var userIdGuid))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Invalid token claims.", type: "https://httpstatuses.com/401");
            }

            var revoked = await authService.RevokeSessionAsync(new UserId(userIdGuid), id, ct);
            if (!revoked)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Session not found",
                    detail: "The specified session was not found.");
            }

            if (Guid.TryParse(sidStr, out var currentSessionId) && currentSessionId == id)
            {
                cookieService.ClearAuthCookies(httpContext.Response, httpContext.Request.IsHttps);
            }

            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("RevokeSession")
        .WithSummary("Revoke a selected session");

        return app;
    }
}
