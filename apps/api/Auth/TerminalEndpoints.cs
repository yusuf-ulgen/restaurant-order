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
                return Results.Unauthorized();
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
            ITrustedTerminalService terminalService,
            CancellationToken ct) =>
        {
            try
            {
                var command = new ActivateTerminalCommand(
                    EnrollmentCode: request.EnrollmentCode,
                    DeviceIdentifier: request.DeviceIdentifier,
                    TerminalName: request.TerminalName);

                var result = await terminalService.ActivateTerminalAsync(command, ct);
                return Results.Ok(result);
            }
            catch (PinAuthFailureException ex)
            {
                return Results.Json(new
                {
                    type = "https://httpstatuses.com/401",
                    title = "Unauthorized",
                    status = 401,
                    detail = ex.Message
                }, statusCode: StatusCodes.Status401Unauthorized, contentType: "application/problem+json");
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
                return Results.Unauthorized();
            }

            var subStr = httpContext.User.FindFirst(JwtClaimNames.Subject)?.Value;
            var actorUserId = Guid.TryParse(subStr, out var userGuid) ? new UserId(userGuid) : UserId.New();

            var revoked = await terminalService.RevokeTerminalAsync(new TenantId(tenantContext.TenantId.Value), id, actorUserId, ct);
            return revoked ? Results.NoContent() : Results.NotFound();
        })
        .RequireAuthorization(policy => policy.RequireAuthenticatedUser())
        .RequirePermission(Permissions.BranchStaffManage)
        .WithName("RevokeTerminal")
        .WithSummary("Revoke trusted terminal and terminate all active sessions tied to it");

        group.MapGet("/current", async (
            HttpContext httpContext,
            ITrustedTerminalService terminalService,
            CancellationToken ct) =>
        {
            var terminalIdHeader = httpContext.Request.Headers["X-Terminal-Id"].FirstOrDefault();
            var deviceSecretHeader = httpContext.Request.Headers["X-Device-Secret"].FirstOrDefault();

            if (!Guid.TryParse(terminalIdHeader, out var terminalId) || string.IsNullOrWhiteSpace(deviceSecretHeader))
            {
                return Results.Unauthorized();
            }

            var context = await terminalService.GetCurrentTerminalAsync(terminalId, deviceSecretHeader, ct);
            if (context == null || !context.IsActive)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(context);
        })
        .WithName("GetCurrentTerminal")
        .WithSummary("Verify and retrieve current trusted terminal metadata");

        return app;
    }
}
