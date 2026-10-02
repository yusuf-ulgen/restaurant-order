using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model representing a global platform SuperAdmin session.
/// Completely decoupled from tenant RLS tables and tenant memberships.
/// </summary>
public sealed class PlatformSession
{
    public Guid Id { get; private set; }
    public UserId UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string CurrentTokenHash { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevocationReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public string ConcurrencyToken { get; private set; }

    private PlatformSession()
    {
        // Required by EF Core
        CurrentTokenHash = string.Empty;
        ConcurrencyToken = string.Empty;
    }

    public static PlatformSession Create(
        UserId userId,
        Guid familyId,
        string tokenHash,
        TimeSpan sessionLifetime,
        DateTimeOffset nowUtc,
        string? ipAddress = null,
        string? userAgent = null,
        Guid? id = null)
    {
        if (familyId == Guid.Empty)
        {
            throw new DomainException("FamilyId cannot be an empty Guid.");
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("TokenHash cannot be empty.");
        }

        return new PlatformSession
        {
            Id = id ?? Guid.CreateVersion7(),
            UserId = userId,
            FamilyId = familyId,
            CurrentTokenHash = tokenHash,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            IsRevoked = false,
            RevokedAtUtc = null,
            RevocationReason = null,
            CreatedAtUtc = nowUtc,
            LastSeenAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(sessionLifetime),
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };
    }

    public void Revoke(string reason, DateTimeOffset nowUtc)
    {
        if (IsRevoked) return;

        IsRevoked = true;
        RevokedAtUtc = nowUtc;
        RevocationReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
        ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

    public void RotateToken(string newTokenHash, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(newTokenHash))
        {
            throw new DomainException("New token hash cannot be empty.");
        }

        CurrentTokenHash = newTokenHash;
        LastSeenAtUtc = nowUtc;
        ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

    public bool IsActive(DateTimeOffset nowUtc) =>
        !IsRevoked && ExpiresAtUtc > nowUtc;
}
