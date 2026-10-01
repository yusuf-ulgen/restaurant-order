namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Provider-agnostic abstraction for delivering security identity notifications
/// (e.g. staff invitations, self-service password reset tokens).
/// </summary>
public interface IIdentityNotificationSender
{
    Task SendInvitationAsync(
        string email,
        string invitationToken,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct = default);

    Task SendPasswordResetAsync(
        string email,
        string resetToken,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct = default);
}
