using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model for staff PIN credentials bound to a specific branch.
/// Stores only the salted, slow-hashed, peppered representation of the PIN.
/// </summary>
public sealed class PinCredential
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public UserId UserId { get; private set; }
    public BranchId BranchId { get; private set; }
    public string PinHash { get; private set; }
    public string AlgorithmVersion { get; private set; }
    public string PepperKeyId { get; private set; }
    public int FailedPinAttempts { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private PinCredential()
    {
        // Required by EF Core
        PinHash = string.Empty;
        AlgorithmVersion = string.Empty;
        PepperKeyId = string.Empty;
    }

    public static PinCredential Create(
        TenantId tenantId,
        UserId userId,
        BranchId branchId,
        string pinHash,
        string algorithmVersion,
        string pepperKeyId,
        DateTimeOffset nowUtc,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(pinHash))
        {
            throw new DomainException("PIN hash cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(algorithmVersion))
        {
            throw new DomainException("Algorithm version cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(pepperKeyId))
        {
            throw new DomainException("Pepper key ID cannot be empty.");
        }

        return new PinCredential
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            BranchId = branchId,
            PinHash = pinHash,
            AlgorithmVersion = algorithmVersion,
            PepperKeyId = pepperKeyId,
            FailedPinAttempts = 0,
            LockedUntilUtc = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    public void UpdatePin(string newPinHash, string algorithmVersion, string pepperKeyId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(newPinHash))
        {
            throw new DomainException("New PIN hash cannot be empty.");
        }

        PinHash = newPinHash;
        AlgorithmVersion = algorithmVersion;
        PepperKeyId = pepperKeyId;
        FailedPinAttempts = 0;
        LockedUntilUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    public void RecordSuccessfulAttempt(DateTimeOffset nowUtc)
    {
        FailedPinAttempts = 0;
        LockedUntilUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    public void RecordFailedAttempt(int maxAttempts, TimeSpan lockoutDuration, DateTimeOffset nowUtc)
    {
        FailedPinAttempts++;
        UpdatedAtUtc = nowUtc;

        if (FailedPinAttempts >= maxAttempts)
        {
            LockedUntilUtc = nowUtc.Add(lockoutDuration);
        }
    }

    public bool IsLocked(DateTimeOffset nowUtc) =>
        LockedUntilUtc.HasValue && nowUtc < LockedUntilUtc.Value;
}
