using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model for an opaque rotating platform refresh token for SuperAdmin.
/// Stored only as cryptographic hash with family rotation and reuse detection.
/// </summary>
public sealed class PlatformRefreshToken
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid TokenFamilyId { get; private set; }
    public string TokenHash { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    private PlatformRefreshToken()
    {
        // Required by EF Core
        TokenHash = string.Empty;
    }

    public static PlatformRefreshToken Create(
        Guid sessionId,
        Guid tokenFamilyId,
        string tokenHash,
        TimeSpan lifetime,
        DateTimeOffset nowUtc,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("PlatformRefreshToken token hash cannot be empty.");
        }

        if (sessionId == Guid.Empty)
        {
            throw new DomainException("SessionId cannot be empty.");
        }

        if (tokenFamilyId == Guid.Empty)
        {
            throw new DomainException("TokenFamilyId cannot be empty.");
        }

        return new PlatformRefreshToken
        {
            Id = id ?? Guid.CreateVersion7(),
            SessionId = sessionId,
            TokenFamilyId = tokenFamilyId,
            TokenHash = tokenHash,
            IsRevoked = false,
            RevokedAtUtc = null,
            ReplacedByTokenId = null,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(lifetime)
        };
    }

    public void Revoke(DateTimeOffset nowUtc, Guid? replacedByTokenId = null)
    {
        IsRevoked = true;
        RevokedAtUtc = nowUtc;
        ReplacedByTokenId = replacedByTokenId;
    }

    public bool IsActive(DateTimeOffset nowUtc) =>
        !IsRevoked && ExpiresAtUtc > nowUtc;
}
