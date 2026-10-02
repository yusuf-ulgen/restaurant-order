using System.Security.Claims;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Api.Auth;

public sealed record EnrollTerminalApiRequest(
    Guid BranchId,
    string TerminalName,
    string? DeviceIdentifier);

public sealed record ActivateTerminalApiRequest(
    string EnrollmentCode,
    string DeviceIdentifier,
    string TerminalName,
    string? TenantSlug = null);

public sealed record ActivateTerminalApiResponse(
    Guid TerminalId,
    Guid TenantId,
    Guid BranchId,
    string TerminalName);

public static class TerminalEndpoints
{
    public static IEndpointRouteBuilder MapTerminalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/terminals")
            .WithTags("Trusted Terminals");

        group.MapPost("/enroll", async (
            EnrollTerminalApiRequest request,
            HttpContext httpContext,
            ITenantContext tenantContext,
            ITrustedTerminalService terminalService,
            CancellationToken ct) =>
        {
            if (!tenantContext.IsAuthenticated || !tenantContext.TenantId.HasValue)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Authentication required.", type: "https://httpstatuses.com/401");
            }

            var branchClaim = httpContext.User.FindFirst(JwtClaimNames.BranchId)?.Value
                ?? httpContext.User.FindFirst("branch_id")?.Value;
            var isBranchManager = httpContext.User.IsInRole("BranchManager")
                || httpContext.User.FindFirst(ClaimTypes.Role)?.Value == "BranchManager";

            if (isBranchManager)
            {
                if (!Guid.TryParse(branchClaim, out var actorBranchId) || actorBranchId != request.BranchId)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "BranchManager cannot enroll terminal for another branch.", type: "https://httpstatuses.com/403");
                }
            }

            var subStr = httpContext.User.FindFirst(JwtClaimNames.Subject)?.Value;
            var actorUserId = Guid.TryParse(subStr, out var userGuid) ? new UserId(userGuid) : UserId.New();

            var command = new EnrollTerminalCommand(
                BranchId: request.BranchId,
                TerminalName: request.TerminalName,
                DeviceIdentifier: request.DeviceIdentifier ?? $"term-{Guid.NewGuid():N}");

            var result = await terminalService.CreateEnrollmentCodeAsync(
                new TenantId(tenantContext.TenantId.Value),
                command,
                actorUserId,
                ct);

            return Results.Ok(result);
        })
        .RequireAuthorization(policy => policy.RequireAuthenticatedUser())
        .RequirePermission(Permissions.BranchStaffManage)
        .WithName("EnrollTerminal")
        .WithSummary("Generate single-use enrollment code for physical terminal registration");

        group.MapPost("/activate", async (
            ActivateTerminalApiRequest request,
            HttpContext httpContext,
            ITrustedTerminalService terminalService,
            IAuthCookieService cookieService,
            CancellationToken ct) =>
        {
            try
            {
                var command = new ActivateTerminalCommand(
                    EnrollmentCode: request.EnrollmentCode,
                    DeviceIdentifier: request.DeviceIdentifier,
                    TerminalName: request.TerminalName);

                var result = await terminalService.ActivateTerminalAsync(command, ct);

                cookieService.SetTerminalCookie(
                    httpContext.Response,
                    result.TerminalId,
                    result.DeviceSecret,
                    httpContext.Request.IsHttps);

                var responseDto = new ActivateTerminalApiResponse(
                    result.TerminalId,
                    result.TenantId,
                    result.BranchId,
                    result.TerminalName);

                return Results.Ok(responseDto);
            }
            catch (PinAuthFailureException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: ex.Message, type: "https://httpstatuses.com/401");
            }
        })
        .WithName("ActivateTerminal")
        .WithSummary("Activate an enrolled terminal and receive hardware secret");

        group.MapPost("/{id:guid}/revoke", async (
            Guid id,
            HttpContext httpContext,
            ITenantContext tenantContext,
            ITrustedTerminalService terminalService,
            CancellationToken ct) =>
        {
            if (!tenantContext.IsAuthenticated || !tenantContext.TenantId.HasValue)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Authentication required.", type: "https://httpstatuses.com/401");
            }

            var subStr = httpContext.User.FindFirst(JwtClaimNames.Subject)?.Value;
            var actorUserId = Guid.TryParse(subStr, out var userGuid) ? new UserId(userGuid) : UserId.New();

            var revoked = await terminalService.RevokeTerminalAsync(new TenantId(tenantContext.TenantId.Value), id, actorUserId, ct);
            return revoked ? Results.NoContent() : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: "Terminal not found or already revoked.", type: "https://httpstatuses.com/404");
        })
        .RequireAuthorization(policy => policy.RequireAuthenticatedUser())
        .RequirePermission(Permissions.BranchStaffManage)
        .WithName("RevokeTerminal")
        .WithSummary("Revoke trusted terminal and terminate all active sessions tied to it");

        group.MapGet("/current", async (
            HttpContext httpContext,
            ITrustedTerminalService terminalService,
            IAuthCookieService cookieService,
            CancellationToken ct) =>
        {
            var creds = cookieService.GetTerminalCredentials(httpContext.Request);
            if (creds == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Trusted terminal credentials required.", type: "https://httpstatuses.com/401");
            }

            var context = await terminalService.GetCurrentTerminalAsync(creds.Value.TerminalId, creds.Value.DeviceSecret, ct);
            if (context == null || !context.IsActive)
            {
                cookieService.ClearTerminalCookie(httpContext.Response, httpContext.Request.IsHttps);
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Terminal is inactive or invalid.", type: "https://httpstatuses.com/401");
            }

            return Results.Ok(context);
        })
        .WithName("GetCurrentTerminal")
        .WithSummary("Verify and retrieve current trusted terminal metadata");

        group.MapPost("/deactivate", (
            HttpContext httpContext,
            IAuthCookieService cookieService) =>
        {
            cookieService.ClearTerminalCookie(httpContext.Response, httpContext.Request.IsHttps);
            cookieService.ClearAuthCookies(httpContext.Response, httpContext.Request.IsHttps);
            return Results.Ok(new { message = "Terminal deactivated successfully." });
        })
        .WithName("DeactivateTerminal")
        .WithSummary("Clear terminal credentials cookie and staff session");

        return app;
    }
}
