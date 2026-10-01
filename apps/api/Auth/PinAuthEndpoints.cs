using System.Security.Claims;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Api.Auth;

public sealed record PinLoginApiRequest(
    Guid? UserId,
    string? Email,
    string Pin);

public sealed record SetPinApiRequest(
    Guid UserId,
    Guid BranchId,
    string Pin);

public static class PinAuthEndpoints
{
    public static IEndpointRouteBuilder MapPinAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth/pin")
            .WithTags("Staff PIN Authentication");

        group.MapPost("/login", async (
            PinLoginApiRequest request,
            HttpContext httpContext,
            IStaffPinAuthService pinAuthService,
            IAuthCookieService cookieService,
            CancellationToken ct) =>
        {
            var creds = cookieService.GetTerminalCredentials(httpContext.Request);
            if (creds == null)
            {
                return Results.Json(new
                {
                    type = "https://httpstatuses.com/401",
                    title = "Unauthorized",
                    status = 401,
                    detail = "Trusted terminal credentials required."
                }, statusCode: StatusCodes.Status401Unauthorized, contentType: "application/problem+json");
            }

            var terminalId = creds.Value.TerminalId;
            var deviceSecret = creds.Value.DeviceSecret;

            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            try
            {
                var command = new PinLoginCommand(
                    TerminalId: terminalId,
                    DeviceSecret: deviceSecret,
                    UserId: request.UserId,
                    Email: request.Email,
                    Pin: request.Pin,
                    IpAddress: ip,
                    UserAgent: userAgent);

                var result = await pinAuthService.LoginWithPinAsync(command, ct);

                cookieService.SetAuthCookies(
                    httpContext.Response,
                    result.AccessToken,
                    result.RefreshToken,
                    result.AccessTokenExpiresAt,
                    result.RefreshTokenExpiresAt,
                    httpContext.Request.IsHttps);

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
            catch (PinAuthFailureException)
            {
                return Results.Json(new
                {
                    type = "https://httpstatuses.com/401",
                    title = "Unauthorized",
                    status = 401,
                    detail = PinAuthFailureException.GenericMessage
                }, statusCode: StatusCodes.Status401Unauthorized, contentType: "application/problem+json");
            }
        })
        .WithName("PinLogin")
        .WithSummary("Fast staff authentication via 4-digit PIN on an active trusted terminal");

        group.MapPost("/logout", async (
            HttpContext httpContext,
            IStaffPinAuthService pinAuthService,
            IAuthCookieService cookieService,
            CancellationToken ct) =>
        {
            var sidStr = httpContext.User.FindFirst(JwtClaimNames.SessionId)?.Value;
            var creds = cookieService.GetTerminalCredentials(httpContext.Request);
            var terminalId = creds?.TerminalId;

            if (Guid.TryParse(sidStr, out var sessionId))
            {
                await pinAuthService.LogoutPinSessionAsync(sessionId, terminalId ?? Guid.Empty, ct);
            }

            cookieService.ClearAuthCookies(httpContext.Response, httpContext.Request.IsHttps);
            // Re-issue fresh CSRF token cookie for the active enrolled terminal session
            cookieService.SetCsrfCookie(httpContext.Response, httpContext.Request.IsHttps);
            return Results.Ok(new { message = "Logged out from terminal successfully." });
        })
        .WithName("PinLogout")
        .WithSummary("Log out staff quick-session while keeping the terminal enrolled");

        group.MapPost("/set", async (
            SetPinApiRequest request,
            HttpContext httpContext,
            ITenantContext tenantContext,
            IStaffPinAuthService pinAuthService,
            CancellationToken ct) =>
        {
            if (!tenantContext.IsAuthenticated || !tenantContext.TenantId.HasValue)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Authentication required.", type: "https://httpstatuses.com/401");
            }

            var subStr = httpContext.User.FindFirst(JwtClaimNames.Subject)?.Value;
            if (!Guid.TryParse(subStr, out var actorGuid))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Invalid token claims.", type: "https://httpstatuses.com/401");
            }

            var isSelf = actorGuid == request.UserId;
            if (!isSelf)
            {
                var isBranchManager = httpContext.User.IsInRole("BranchManager")
                    || httpContext.User.FindFirst(ClaimTypes.Role)?.Value == "BranchManager";

                var hasStaffManagePermission = httpContext.User.HasClaim("perm", Permissions.BranchStaffManage)
                    || httpContext.User.IsInRole("RestaurantAdmin")
                    || isBranchManager;

                if (!hasStaffManagePermission)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "Staff management permission required.", type: "https://httpstatuses.com/403");
                }

                if (isBranchManager)
                {
                    var branchClaim = httpContext.User.FindFirst(JwtClaimNames.BranchId)?.Value
                        ?? httpContext.User.FindFirst("branch_id")?.Value;
                    if (!Guid.TryParse(branchClaim, out var actorBranchId) || actorBranchId != request.BranchId)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "BranchManager cannot configure PIN for another branch.", type: "https://httpstatuses.com/403");
                    }
                }
            }

            try
            {
                var command = new SetStaffPinCommand(request.UserId, request.BranchId, request.Pin);
                await pinAuthService.SetPinAsync(new RestaurantOrder.Domain.Tenants.TenantId(tenantContext.TenantId.Value), command, new UserId(actorGuid), ct);

                return Results.Ok(new { message = "Staff PIN updated successfully." });
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ex.Message, type: "https://httpstatuses.com/400");
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ex.Message, type: "https://httpstatuses.com/400");
            }
        })
        .RequireAuthorization()
        .WithName("SetStaffPin")
        .WithSummary("Set or update 4-digit PIN for staff member");

        return app;
    }
}
