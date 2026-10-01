using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Service contract for staff provisioning, role-scoped invitations,
/// single-use activation tokens, password reset flows, and staff lifecycle operations.
/// </summary>
public interface IStaffIdentityService
{
    Task<InviteStaffResult> InviteStaffAsync(
        TenantId? tenantId,
        InviteStaffCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task AcceptInvitationAsync(
        AcceptInvitationCommand command,
        CancellationToken ct = default);

    Task<string?> RequestPasswordResetAsync(
        RequestPasswordResetCommand command,
        CancellationToken ct = default);

    Task ResetPasswordAsync(
        ResetPasswordCommand command,
        CancellationToken ct = default);

    Task<IReadOnlyList<StaffMemberDto>> ListStaffAsync(
        TenantId? tenantId,
        Guid? branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task UpdateStaffStatusAsync(
        TenantId? tenantId,
        UpdateStaffStatusCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);
}
