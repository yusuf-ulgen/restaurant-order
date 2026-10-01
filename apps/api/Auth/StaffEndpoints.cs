using System.Security.Claims;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Api.Auth;

public sealed record InviteStaffApiRequest(
    string Email,
    string Role,
    Guid? BranchId = null);

public sealed record AcceptInvitationApiRequest(
    string InvitationToken,
    string Password);

public sealed record ForgotPasswordApiRequest(
    string Email,
    string? TenantSlug = null);

public sealed record ResetPasswordApiRequest(
    string ResetToken,
    string NewPassword);

public sealed record UpdateStaffStatusApiRequest(
    string Status);

public static class StaffEndpoints
{
    public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/staff")
            .WithTags("Staff Identity Management");

        group.MapPost("/invite", async (
            InviteStaffApiRequest request,
            HttpContext httpContext,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            IStaffIdentityService staffService,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<AuthRole>(request.Role, ignoreCase: true, out var role))
            {
                return Results.BadRequest(new { error = $"Invalid role '{request.Role}'." });
            }

            var claimsDict = httpContext.User.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
            var actor = parser.ParsePrincipal(claimsDict);

            TenantId? tenantId = tenantContext.TenantId.HasValue ? new TenantId(tenantContext.TenantId.Value) : null;
            var command = new InviteStaffCommand(request.Email, role, request.BranchId);

            try
            {
                var result = await staffService.InviteStaffAsync(tenantId, command, actor, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchStaffManage)
        .WithName("InviteStaff")
        .WithSummary("Invite new staff member with role and branch assignment");

        group.MapPost("/accept-invitation", async (
            AcceptInvitationApiRequest request,
            IStaffIdentityService staffService,
            CancellationToken ct) =>
        {
            try
            {
                var command = new AcceptInvitationCommand(request.InvitationToken, request.Password);
                await staffService.AcceptInvitationAsync(command, ct);
                return Results.Ok(new { message = "Invitation accepted successfully." });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("AcceptInvitation")
        .WithSummary("Activate staff account and set initial password using single-use invitation token");

        group.MapPost("/forgot-password", async (
            ForgotPasswordApiRequest request,
            IStaffIdentityService staffService,
            CancellationToken ct) =>
        {
            var command = new RequestPasswordResetCommand(request.Email, request.TenantSlug);
            await staffService.RequestPasswordResetAsync(command, ct);

            // Generic non-enumerating response
            return Results.Ok(new { message = "If the account exists, a password reset link has been dispatched." });
        })
        .WithName("ForgotPassword")
        .WithSummary("Initiate self-service password reset (non-enumerating)");

        group.MapPost("/reset-password", async (
            ResetPasswordApiRequest request,
            IStaffIdentityService staffService,
            CancellationToken ct) =>
        {
            try
            {
                var command = new ResetPasswordCommand(request.ResetToken, request.NewPassword);
                await staffService.ResetPasswordAsync(command, ct);
                return Results.Ok(new { message = "Password reset successfully." });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ResetPassword")
        .WithSummary("Complete self-service password reset using single-use reset token");

        group.MapGet("/", async (
            Guid? branchId,
            HttpContext httpContext,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            IStaffIdentityService staffService,
            CancellationToken ct) =>
        {
            var claimsDict = httpContext.User.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
            var actor = parser.ParsePrincipal(claimsDict);

            TenantId? tenantId = tenantContext.TenantId.HasValue ? new TenantId(tenantContext.TenantId.Value) : null;
            var list = await staffService.ListStaffAsync(tenantId, branchId, actor, ct);
            return Results.Ok(list);
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchStaffManage)
        .WithName("ListStaff")
        .WithSummary("List staff members within tenant and optional branch");

        group.MapPost("/{id:guid}/status", async (
            Guid id,
            UpdateStaffStatusApiRequest request,
            HttpContext httpContext,
            ITenantContext tenantContext,
            IJwtClaimPrincipalParser parser,
            IStaffIdentityService staffService,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<UserStatus>(request.Status, ignoreCase: true, out var status))
            {
                return Results.BadRequest(new { error = $"Invalid status '{request.Status}'." });
            }

            var claimsDict = httpContext.User.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
            var actor = parser.ParsePrincipal(claimsDict);

            TenantId? tenantId = tenantContext.TenantId.HasValue ? new TenantId(tenantContext.TenantId.Value) : null;
            var command = new UpdateStaffStatusCommand(id, status);

            try
            {
                await staffService.UpdateStaffStatusAsync(tenantId, command, actor, ct);
                return Results.Ok(new { message = "Staff status updated successfully." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchStaffManage)
        .WithName("UpdateStaffStatus")
        .WithSummary("Update staff lifecycle status (Active, Suspended, Disabled)");

        return app;
    }
}
