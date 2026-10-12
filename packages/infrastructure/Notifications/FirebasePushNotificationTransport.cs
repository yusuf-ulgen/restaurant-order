using System.Globalization;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2.Responses;
using RestaurantOrder.Application.Notifications;

namespace RestaurantOrder.Infrastructure.Notifications;

public sealed class FirebasePushNotificationTransport : IPushNotificationTransport, IDisposable
{
    private readonly FirebaseApp app;
    private readonly FirebaseMessaging messaging;
    private readonly TimeProvider timeProvider;

    public FirebasePushNotificationTransport(AppOptions options, TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        app = FirebaseApp.Create(options, $"restaurant-order-push-{Guid.NewGuid():N}");
        messaging = FirebaseMessaging.GetMessaging(app);
    }

    public async Task<PushSendResult> SendAsync(PushNotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var remaining = request.ExpiresAt - now;
        var ttl = (int)Math.Floor(Math.Min(remaining.TotalSeconds, 60));
        if (ttl < 1)
        {
            return new(PushSendStatus.Expired);
        }

        var message = new Message
        {
            Fid = request.InstallationId,
            Notification = new Notification
            {
                Title = "Restoran bildirimi",
                Body = "Yeni bir güncelleme var. Ayrıntılar için uygulamayı açın."
            },
            Data = new Dictionary<string, string>
            {
                ["event_id"] = request.EventId.ToString("N"),
                ["expires_at"] = now.AddSeconds(ttl).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            },
            Webpush = new WebpushConfig
            {
                Headers = new Dictionary<string, string> { ["TTL"] = ttl.ToString(CultureInfo.InvariantCulture) }
            },
            Android = new AndroidConfig { TimeToLive = TimeSpan.FromSeconds(ttl) },
            Apns = new ApnsConfig
            {
                Headers = new Dictionary<string, string>
                {
                    ["apns-expiration"] = now.AddSeconds(ttl).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
                }
            }
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(remaining.TotalSeconds, 10)));
        try
        {
            await messaging.SendAsync(message, timeout.Token);
            return new(PushSendStatus.ProviderAccepted);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Sonuç belirsiz olabilir; otomatik olarak aynı bildirimi yeniden göndermeyiz.
            return new(PushSendStatus.RetryableFailure);
        }
        catch (TokenResponseException)
        {
            // ADC yenileme hatasının açıklaması kimlik bilgisi ayrıntıları içerebilir.
            return new(PushSendStatus.ConfigurationFailure);
        }
        catch (FirebaseMessagingException exception)
        {
            var status = exception.MessagingErrorCode switch
            {
                MessagingErrorCode.Unregistered => PushSendStatus.InvalidRecipient,
                MessagingErrorCode.SenderIdMismatch or MessagingErrorCode.ThirdPartyAuthError => PushSendStatus.ConfigurationFailure,
                MessagingErrorCode.InvalidArgument => PushSendStatus.PermanentFailure,
                MessagingErrorCode.QuotaExceeded or MessagingErrorCode.Unavailable or MessagingErrorCode.Internal => PushSendStatus.RetryableFailure,
                _ when exception.ErrorCode is ErrorCode.Unauthenticated or ErrorCode.PermissionDenied or ErrorCode.NotFound => PushSendStatus.ConfigurationFailure,
                _ when exception.ErrorCode == ErrorCode.InvalidArgument => PushSendStatus.PermanentFailure,
                _ => PushSendStatus.RetryableFailure
            };
            var header = exception.HttpResponse?.Headers.RetryAfter;
            var retryAfter = header?.Delta ?? (header?.Date - timeProvider.GetUtcNow());
            if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
            if ((exception.MessagingErrorCode == MessagingErrorCode.QuotaExceeded || exception.ErrorCode == ErrorCode.ResourceExhausted) &&
                (retryAfter is null || retryAfter < TimeSpan.FromMinutes(1)))
            {
                retryAfter = TimeSpan.FromMinutes(1);
            }

            // Sağlayıcı hata gövdesi ve cihaz kimliği sonuç veya günlük akışına taşınmaz.
            return new(status, status == PushSendStatus.RetryableFailure ? retryAfter : null);
        }
    }

    public void Dispose() => app.Delete();
}
