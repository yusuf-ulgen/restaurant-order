using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Application.Auth;

public sealed record InviteStaffCommand(
    string Email,
    AuthRole Role,
    Guid? BranchId = null);

public sealed record InviteStaffResult(
    Guid UserId,
    string Email,
    AuthRole Role,
    Guid? BranchId,
    string InvitationToken,
    DateTimeOffset ExpiresAtUtc);

public sealed record AcceptInvitationCommand(
    string InvitationToken,
    string Password);

public sealed record RequestPasswordResetCommand(
    string Email,
    string? TenantSlug = null);

public sealed record ResetPasswordCommand(
    string ResetToken,
    string NewPassword);

public sealed record UpdateStaffStatusCommand(
    Guid UserId,
    UserStatus Status);

public sealed record StaffMemberDto(
    Guid UserId,
    string Email,
    string Role,
    Guid? BranchId,
    string Status,
    DateTimeOffset CreatedAtUtc);
