using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Notifications;

/// <summary>
/// Domain entity representing an authorized device/installation registered for push notifications.
/// Bounded strictly to an authenticated staff user or active customer dining session.
/// </summary>
public sealed class DeviceRegistration
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public UserId? UserId { get; private set; }
    public DiningSessionId? DiningSessionId { get; private set; }
    public string InstallationId { get; private set; }
    public string Platform { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    private DeviceRegistration()
    {
        InstallationId = string.Empty;
        Platform = string.Empty;
    }

    public static DeviceRegistration CreateForStaff(
        TenantId tenantId,
        BranchId branchId,
        UserId userId,
        string installationId,
        string platform,
        DateTimeOffset createdAtUtc,
        TimeSpan ttl,
        Guid? id = null)
    {
        ValidateInstallationId(installationId);
        ValidatePlatform(platform);

        return new DeviceRegistration
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            UserId = userId,
            DiningSessionId = null,
            InstallationId = installationId.Trim(),
            Platform = platform.Trim().ToLowerInvariant(),
            IsActive = true,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            ExpiresAtUtc = createdAtUtc.Add(ttl)
        };
    }

    public static DeviceRegistration CreateForCustomer(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId diningSessionId,
        string installationId,
        string platform,
        DateTimeOffset createdAtUtc,
        TimeSpan ttl,
        Guid? id = null)
    {
        ValidateInstallationId(installationId);
        ValidatePlatform(platform);

        return new DeviceRegistration
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            UserId = null,
            DiningSessionId = diningSessionId,
            InstallationId = installationId.Trim(),
            Platform = platform.Trim().ToLowerInvariant(),
            IsActive = true,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            ExpiresAtUtc = createdAtUtc.Add(ttl)
        };
    }

    public void Renew(DateTimeOffset expiresAtUtc, DateTimeOffset updatedAtUtc)
    {
        IsActive = true;
        ExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void RebindToCustomer(
        BranchId branchId,
        DiningSessionId diningSessionId,
        string platform,
        DateTimeOffset updatedAtUtc,
        TimeSpan ttl)
    {
        ValidatePlatform(platform);
        BranchId = branchId;
        DiningSessionId = diningSessionId;
        UserId = null;
        Platform = platform.Trim().ToLowerInvariant();
        IsActive = true;
        UpdatedAtUtc = updatedAtUtc;
        ExpiresAtUtc = updatedAtUtc.Add(ttl);
    }

    public void RebindToStaff(
        BranchId branchId,
        UserId userId,
        string platform,
        DateTimeOffset updatedAtUtc,
        TimeSpan ttl)
    {
        ValidatePlatform(platform);
        BranchId = branchId;
        UserId = userId;
        DiningSessionId = null;
        Platform = platform.Trim().ToLowerInvariant();
        IsActive = true;
        UpdatedAtUtc = updatedAtUtc;
        ExpiresAtUtc = updatedAtUtc.Add(ttl);
    }

    public void Deactivate(DateTimeOffset updatedAtUtc)
    {
        IsActive = false;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static void ValidateInstallationId(string installationId)
    {
        if (string.IsNullOrWhiteSpace(installationId) || installationId.Length > 256 ||
            installationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
        {
            throw new DomainException("Invalid device installation identifier format.");
        }
    }

    private static void ValidatePlatform(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
            throw new DomainException("Platform cannot be empty.");

        var normalized = platform.Trim().ToLowerInvariant();
        if (normalized != "web" && normalized != "android" && normalized != "ios")
            throw new DomainException($"Unsupported platform '{platform}'. Must be web, android, or ios.");
    }
}
