using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model for an opaque rotating refresh token.
/// Persists only the cryptographic hash of the token.
/// Tracks family-based rotation and reuse detection state.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid TokenFamilyId { get; private set; }
    public string TokenHash { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    private RefreshToken()
    {
        // Required by EF Core
        TokenHash = string.Empty;
    }

    public static RefreshToken Create(
        TenantId tenantId,
        Guid sessionId,
        Guid tokenFamilyId,
        string tokenHash,
        TimeSpan lifetime,
        DateTimeOffset nowUtc,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("RefreshToken token hash cannot be empty.");
        }

        if (sessionId == Guid.Empty)
        {
            throw new DomainException("SessionId cannot be empty.");
        }

        if (tokenFamilyId == Guid.Empty)
        {
            throw new DomainException("TokenFamilyId cannot be empty.");
        }

        return new RefreshToken
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
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
