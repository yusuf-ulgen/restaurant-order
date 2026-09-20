using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Brands;

/// <summary>
/// Strongly typed identifier for a Brand within a Tenant.
/// Backed by a sortable UUIDv7 to ensure database index locality and prevent enumeration attacks.
/// </summary>
public readonly record struct BrandId
{
    public Guid Value { get; }

    public BrandId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("BrandId cannot be an empty Guid.");
        }

        Value = value;
    }

    public static BrandId New() => new(Guid.CreateVersion7());

    public static BrandId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(BrandId id) => id.Value;
}
