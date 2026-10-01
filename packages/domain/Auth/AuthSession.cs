using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model representing an active authentication session.
/// Bound strictly to a Tenant and User identity, tracking revocation and lifetime.
/// </summary>
public sealed class AuthSession
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public UserId UserId { get; private set; }
    public Guid MembershipId { get; private set; }
    public BranchId? BranchId { get; private set; }
    public AuthenticationMethod AuthMethod { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevocationReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    private AuthSession()
    {
        // Required by EF Core
    }

    public static AuthSession Create(
        TenantId tenantId,
        UserId userId,
        Guid membershipId,
        BranchId? branchId,
        AuthenticationMethod authMethod,
        TimeSpan sessionLifetime,
        DateTimeOffset nowUtc,
        string? ipAddress = null,
        string? userAgent = null,
        Guid? id = null)
    {
        if (membershipId == Guid.Empty)
        {
            throw new DomainException("MembershipId cannot be an empty Guid.");
        }

        return new AuthSession
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            MembershipId = membershipId,
            BranchId = branchId,
            AuthMethod = authMethod,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            IsRevoked = false,
            RevokedAtUtc = null,
            RevocationReason = null,
            CreatedAtUtc = nowUtc,
            LastSeenAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(sessionLifetime)
        };
    }

    public void Revoke(string reason, DateTimeOffset nowUtc)
    {
        if (IsRevoked) return;

        IsRevoked = true;
        RevokedAtUtc = nowUtc;
        RevocationReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
    }

    public void RecordActivity(DateTimeOffset nowUtc)
    {
        LastSeenAtUtc = nowUtc;
    }

    public bool IsActive(DateTimeOffset nowUtc) =>
        !IsRevoked && ExpiresAtUtc > nowUtc;
}
