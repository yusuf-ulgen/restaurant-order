using System;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Notifications;

public enum NotificationDeliveryStatus
{
    Pending = 0,
    Claimed = 1,
    ProviderAccepted = 2,
    FailedRetryable = 3,
    FailedPermanent = 4,
    Expired = 5
}

/// <summary>
/// Persistent delivery task for a push notification to an authorized recipient device.
/// Decouples real-time event broadcasting from cloud push transport.
/// Manages lease-based claims, attempt backoff, pre-send authorization checks, and dead-lettering.
/// </summary>
public sealed class NotificationDelivery
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; }
    public Guid DeviceRegistrationId { get; private set; }
    public string InstallationId { get; private set; }
    public string RecipientType { get; private set; }
    public Guid RecipientId { get; private set; }
    public NotificationDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset? NextAttemptUtc { get; private set; }
    public string? LockClaimId { get; private set; }
    public DateTimeOffset? LockExpiresUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public string? LastError { get; private set; }

    private NotificationDelivery()
    {
        EventType = string.Empty;
        InstallationId = string.Empty;
        RecipientType = string.Empty;
    }

    public static NotificationDelivery Create(
        TenantId tenantId,
        BranchId branchId,
        Guid eventId,
        string eventType,
        Guid deviceRegistrationId,
        string installationId,
        string recipientType,
        Guid recipientId,
        DateTimeOffset nowUtc,
        TimeSpan eventTtl,
        int maxAttempts = 3,
        Guid? id = null)
    {
        if (eventId == Guid.Empty) throw new DomainException("EventId cannot be empty.");
        if (string.IsNullOrWhiteSpace(eventType)) throw new DomainException("EventType cannot be empty.");
        if (deviceRegistrationId == Guid.Empty) throw new DomainException("DeviceRegistrationId cannot be empty.");
        if (string.IsNullOrWhiteSpace(installationId)) throw new DomainException("InstallationId cannot be empty.");
        if (recipientId == Guid.Empty) throw new DomainException("RecipientId cannot be empty.");
        if (eventTtl <= TimeSpan.Zero) throw new DomainException("Event TTL must be positive.");
        if (maxAttempts < 1) maxAttempts = 3;

        return new NotificationDelivery
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            EventId = eventId,
            EventType = eventType.Trim(),
            DeviceRegistrationId = deviceRegistrationId,
            InstallationId = installationId.Trim(),
            RecipientType = recipientType.Trim(),
            RecipientId = recipientId,
            Status = NotificationDeliveryStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = maxAttempts,
            NextAttemptUtc = null,
            LockClaimId = null,
            LockExpiresUtc = null,
            ExpiresAtUtc = nowUtc.Add(eventTtl),
            CreatedAtUtc = nowUtc,
            ProcessedAtUtc = null,
            LastError = null
        };
    }

    public void Claim(string claimId, DateTimeOffset lockExpiresUtc)
    {
        if (string.IsNullOrWhiteSpace(claimId)) throw new DomainException("ClaimId cannot be empty.");

        Status = NotificationDeliveryStatus.Claimed;
        LockClaimId = claimId;
        LockExpiresUtc = lockExpiresUtc;
        AttemptCount++;
    }

    public void MarkProviderAccepted(DateTimeOffset processedAtUtc)
    {
        Status = NotificationDeliveryStatus.ProviderAccepted;
        ProcessedAtUtc = processedAtUtc;
        LockClaimId = null;
        LockExpiresUtc = null;
    }

    public void MarkRetryableFailure(string error, TimeSpan retryDelay, DateTimeOffset nowUtc)
    {
        if (AttemptCount >= MaxAttempts)
        {
            Status = NotificationDeliveryStatus.FailedPermanent;
            ProcessedAtUtc = nowUtc;
        }
        else
        {
            Status = NotificationDeliveryStatus.FailedRetryable;
            NextAttemptUtc = nowUtc.Add(retryDelay);
        }

        LastError = TruncateError(error);
        LockClaimId = null;
        LockExpiresUtc = null;
    }

    public void MarkPermanentFailure(string error, DateTimeOffset nowUtc)
    {
        Status = NotificationDeliveryStatus.FailedPermanent;
        LastError = TruncateError(error);
        ProcessedAtUtc = nowUtc;
        LockClaimId = null;
        LockExpiresUtc = null;
    }

    public void MarkExpired(DateTimeOffset nowUtc)
    {
        Status = NotificationDeliveryStatus.Expired;
        LastError = "Event expired before delivery.";
        ProcessedAtUtc = nowUtc;
        LockClaimId = null;
        LockExpiresUtc = null;
    }

    private static string? TruncateError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return null;
        var trimmed = error.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}
