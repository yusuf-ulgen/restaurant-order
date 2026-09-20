using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Tenants;

/// <summary>
/// Strongly typed identifier for a Tenant organization.
/// Backed by a sortable UUIDv7 to ensure database index locality and prevent enumeration attacks.
/// </summary>
public readonly record struct TenantId
{
    public Guid Value { get; }

    public TenantId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("TenantId cannot be an empty Guid.");
        }

        Value = value;
    }

    public static TenantId New() => new(Guid.CreateVersion7());

    public static TenantId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(TenantId id) => id.Value;
}
