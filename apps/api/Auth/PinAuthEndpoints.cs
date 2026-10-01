using System.Security.Claims;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Api.Auth;

public sealed record PinLoginApiRequest(
    Guid? TerminalId,
    string? DeviceSecret,
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
            var terminalId = creds?.TerminalId ?? request.TerminalId;
            var deviceSecret = creds?.DeviceSecret ?? request.DeviceSecret;

            if (terminalId == null || terminalId == Guid.Empty)
            {
                var terminalIdHeader = httpContext.Request.Headers["X-Terminal-Id"].FirstOrDefault();
                if (Guid.TryParse(terminalIdHeader, out var tid))
                {
                    terminalId = tid;
                }
            }

            if (string.IsNullOrWhiteSpace(deviceSecret))
            {
                deviceSecret = httpContext.Request.Headers["X-Device-Secret"].FirstOrDefault();
            }

            if (terminalId == null || terminalId == Guid.Empty || string.IsNullOrWhiteSpace(deviceSecret))
            {
                return Results.Json(new
                {
                    type = "https://httpstatuses.com/401",
                    title = "Unauthorized",
                    status = 401,
                    detail = "Trusted terminal credentials required."
                }, statusCode: StatusCodes.Status401Unauthorized, contentType: "application/problem+json");
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            try
            {
                var command = new PinLoginCommand(
                    TerminalId: terminalId.Value,
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
            var terminalIdHeader = httpContext.Request.Headers["X-Terminal-Id"].FirstOrDefault();
            Guid.TryParse(terminalIdHeader, out var terminalId);

            if (Guid.TryParse(sidStr, out var sessionId))
            {
                await pinAuthService.LogoutPinSessionAsync(sessionId, terminalId, ct);
            }

            cookieService.ClearAuthCookies(httpContext.Response, httpContext.Request.IsHttps);
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
                return Results.Unauthorized();
            }

            var subStr = httpContext.User.FindFirst(JwtClaimNames.Subject)?.Value;
            if (!Guid.TryParse(subStr, out var actorGuid))
            {
                return Results.Unauthorized();
            }

            var isSelf = actorGuid == request.UserId;
            var hasStaffManagePermission = httpContext.User.HasClaim("perm", Permissions.BranchStaffManage)
                || httpContext.User.IsInRole("RestaurantAdmin")
                || httpContext.User.IsInRole("BranchManager");

            if (!isSelf && !hasStaffManagePermission)
            {
                return Results.Forbid();
            }

            try
            {
                var command = new SetStaffPinCommand(request.UserId, request.BranchId, request.Pin);
                await pinAuthService.SetPinAsync(new RestaurantOrder.Domain.Tenants.TenantId(tenantContext.TenantId.Value), command, new UserId(actorGuid), ct);

                return Results.Ok(new { message = "Staff PIN updated successfully." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .RequireAuthorization()
        .WithName("SetStaffPin")
        .WithSummary("Set or update 4-digit PIN for staff member");

        return app;
    }
}
