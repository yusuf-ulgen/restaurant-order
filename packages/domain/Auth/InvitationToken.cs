using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model for an invitation token metadata record.
/// Persists only the cryptographic hash of the invitation token.
/// </summary>
public sealed class InvitationToken
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public UserId UserId { get; private set; }
    public string TokenHash { get; private set; }
    public bool IsConsumed { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private InvitationToken()
    {
        // Required by EF Core
        TokenHash = string.Empty;
    }

    public static InvitationToken Create(
        TenantId tenantId,
        UserId userId,
        string tokenHash,
        TimeSpan validityDuration,
        DateTimeOffset nowUtc,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("Invitation token hash cannot be empty.");
        }

        return new InvitationToken
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
            throw new DomainException("Invitation token has already been consumed.");
        }

        if (nowUtc > ExpiresAtUtc)
        {
            throw new DomainException("Invitation token has expired.");
        }

        IsConsumed = true;
        ConsumedAtUtc = nowUtc;
    }

    public bool IsValid(DateTimeOffset nowUtc) =>
        !IsConsumed && ExpiresAtUtc > nowUtc;
}
