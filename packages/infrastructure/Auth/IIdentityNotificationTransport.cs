namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Delivery transport for identity notifications (e.g. staff invitations, password reset tokens).
/// </summary>
public interface IIdentityNotificationTransport
{
    Task SendAsync(
        string notificationType,
        string recipientEmail,
        string plaintextPayloadJson,
        string idempotencyKey,
        CancellationToken ct = default);
}
