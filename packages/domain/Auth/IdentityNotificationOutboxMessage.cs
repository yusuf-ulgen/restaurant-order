using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

public enum IdentityNotificationStatus
{
    Pending = 1,
    Processing = 2,
    Delivered = 3,
    DeadLetter = 4
}

/// <summary>
/// Domain entity representing an outbox record for asynchronous delivery of identity notifications
/// (e.g., staff invitations, password reset tokens) via webhooks or external transports.
/// </summary>
public sealed class IdentityNotificationOutboxMessage
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string NotificationType { get; private set; }
    public string RecipientEmail { get; private set; }
    public string EncryptedPayload { get; private set; }
    public string IdempotencyKey { get; private set; }
    public IdentityNotificationStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset NextAttemptUtc { get; private set; }
    public string? LastError { get; private set; }
    public Guid? ClaimToken { get; private set; }
    public DateTimeOffset? ClaimedAtUtc { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private IdentityNotificationOutboxMessage()
    {
        NotificationType = string.Empty;
        RecipientEmail = string.Empty;
        EncryptedPayload = string.Empty;
        IdempotencyKey = string.Empty;
    }

    public static IdentityNotificationOutboxMessage Create(
        TenantId tenantId,
        string notificationType,
        string recipientEmail,
        string encryptedPayload,
        string idempotencyKey,
        DateTimeOffset nowUtc,
        int maxAttempts = 5,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(notificationType))
        {
            throw new DomainException("Notification type cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            throw new DomainException("Recipient email cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(encryptedPayload))
        {
            throw new DomainException("Encrypted payload cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainException("Idempotency key cannot be empty.");
        }

        return new IdentityNotificationOutboxMessage
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            NotificationType = notificationType.Trim(),
            RecipientEmail = recipientEmail.Trim().ToLowerInvariant(),
            EncryptedPayload = encryptedPayload,
            IdempotencyKey = idempotencyKey.Trim(),
            Status = IdentityNotificationStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = maxAttempts > 0 ? maxAttempts : 5,
            NextAttemptUtc = nowUtc,
            LastError = null,
            ClaimToken = null,
            ClaimedAtUtc = null,
            LockedUntilUtc = null,
            DeliveredAtUtc = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    public void Claim(Guid claimToken, DateTimeOffset claimedAtUtc, DateTimeOffset lockedUntilUtc)
    {
        Status = IdentityNotificationStatus.Processing;
        ClaimToken = claimToken;
        ClaimedAtUtc = claimedAtUtc;
        LockedUntilUtc = lockedUntilUtc;
        AttemptCount++;
        UpdatedAtUtc = claimedAtUtc;
    }

    public void MarkDelivered(DateTimeOffset nowUtc)
    {
        Status = IdentityNotificationStatus.Delivered;
        ClaimToken = null;
        ClaimedAtUtc = null;
        LockedUntilUtc = null;
        DeliveredAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkFailed(string error, DateTimeOffset nextAttemptUtc, bool isDeadLetter, DateTimeOffset nowUtc)
    {
        Status = isDeadLetter ? IdentityNotificationStatus.DeadLetter : IdentityNotificationStatus.Pending;
        ClaimToken = null;
        ClaimedAtUtc = null;
        LockedUntilUtc = null;
        LastError = error != null && error.Length > 500 ? error[..500] : error;
        NextAttemptUtc = nextAttemptUtc;
        UpdatedAtUtc = nowUtc;
    }
}
