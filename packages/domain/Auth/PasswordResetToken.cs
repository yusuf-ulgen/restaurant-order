using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model for a password reset token metadata record.
/// Persists only the cryptographic hash of the password reset token.
/// </summary>
public sealed class PasswordResetToken
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public UserId UserId { get; private set; }
    public string TokenHash { get; private set; }
    public bool IsConsumed { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private PasswordResetToken()
    {
        // Required by EF Core
        TokenHash = string.Empty;
    }

    public static PasswordResetToken Create(
        TenantId tenantId,
        UserId userId,
        string tokenHash,
        TimeSpan validityDuration,
        DateTimeOffset nowUtc,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("Password reset token hash cannot be empty.");
        }

        return new PasswordResetToken
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            TokenHash = tokenHash,
            IsConsumed = false,
            ConsumedAtUtc = null,
            ExpiresAtUtc = nowUtc.Add(validityDuration),
            CreatedAtUtc = nowUtc
        };
    }

    public void Consume(DateTimeOffset nowUtc)
    {
        if (IsConsumed)
        {
            throw new DomainException("Password reset token has already been consumed.");
        }

        if (nowUtc > ExpiresAtUtc)
        {
            throw new DomainException("Password reset token has expired.");
        }

        IsConsumed = true;
        ConsumedAtUtc = nowUtc;
    }

    public bool IsValid(DateTimeOffset nowUtc) =>
        !IsConsumed && ExpiresAtUtc > nowUtc;
}
