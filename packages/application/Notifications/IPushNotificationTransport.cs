namespace RestaurantOrder.Application.Notifications;

/// <summary>
/// Yalnızca önceden yetkilendirilmiş alıcıya taşıma yapar. Alıcı seçimi, güncel
/// işletme/şube/oturum denetimi ve kalıcı yeniden deneme çağıranın sorumluluğudur.
/// </summary>
public interface IPushNotificationTransport
{
    Task<PushSendResult> SendAsync(PushNotificationRequest request, CancellationToken cancellationToken = default);
}

public enum PushSendStatus
{
    ProviderAccepted,
    Disabled,
    Expired,
    InvalidRecipient,
    RetryableFailure,
    ConfigurationFailure,
    PermanentFailure
}

// ProviderAccepted yalnızca sağlayıcı kabulüdür; cihaz teslimi değildir.
public sealed record PushSendResult(PushSendStatus Status, TimeSpan? RetryAfter = null);
