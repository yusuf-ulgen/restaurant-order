using RestaurantOrder.Application.Notifications;

namespace RestaurantOrder.Infrastructure.Notifications;

public sealed class DisabledPushNotificationTransport : IPushNotificationTransport
{
    public Task<PushSendResult> SendAsync(PushNotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PushSendResult(PushSendStatus.Disabled));
    }
}
