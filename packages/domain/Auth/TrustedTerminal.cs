using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model for an enrolled, trusted physical terminal or device bound to a Branch.
/// Essential for authorizing staff PIN quick-switch operations.
/// </summary>
public sealed class TrustedTerminal
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public string DeviceIdentifier { get; private set; }
    public string TerminalName { get; private set; }
    public string SecretHash { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; private set; }
    public DateTimeOffset EnrolledAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    private TrustedTerminal()
    {
        // Required by EF Core
        DeviceIdentifier = string.Empty;
        TerminalName = string.Empty;
        SecretHash = string.Empty;
    }

    public static TrustedTerminal Enroll(
        TenantId tenantId,
        BranchId branchId,
        string deviceIdentifier,
        string terminalName,
        string secretHash,
        DateTimeOffset nowUtc,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(deviceIdentifier))
        {
            throw new DomainException("Device identifier cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(terminalName))
        {
            throw new DomainException("Terminal name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(secretHash))
        {
            throw new DomainException("Device secret hash cannot be empty.");
        }

        return new TrustedTerminal
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            DeviceIdentifier = deviceIdentifier.Trim(),
            TerminalName = terminalName.Trim(),
            SecretHash = secretHash,
            IsActive = true,
            LastHeartbeatAtUtc = null,
            EnrolledAtUtc = nowUtc,
            RevokedAtUtc = null
        };
    }

    public void Revoke(DateTimeOffset nowUtc)
    {
        IsActive = false;
        RevokedAtUtc = nowUtc;
    }

    public void RecordHeartbeat(DateTimeOffset nowUtc)
    {
        LastHeartbeatAtUtc = nowUtc;
    }
}
