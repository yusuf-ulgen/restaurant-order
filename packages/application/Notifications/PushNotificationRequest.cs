namespace RestaurantOrder.Application.Notifications;

public sealed class PushNotificationRequest
{
    public PushNotificationRequest(string installationId, Guid eventId, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(installationId) || installationId.Length > 256 ||
            installationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
        {
            throw new ArgumentException("Geçerli bir cihaz kurulum kimliği gerekir.", nameof(installationId));
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Opak olay kimliği boş olamaz.", nameof(eventId));
        }

        InstallationId = installationId;
        EventId = eventId;
        ExpiresAt = expiresAt;
    }

    public string InstallationId { get; }
    public Guid EventId { get; }
    public DateTimeOffset ExpiresAt { get; }

    // Otomatik günlük biçimlendirmesi cihaz kimliğini açığa çıkarmamalı.
    public override string ToString() => nameof(PushNotificationRequest);
}
