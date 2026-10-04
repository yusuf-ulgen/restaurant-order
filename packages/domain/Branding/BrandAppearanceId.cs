using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branding;

/// <summary>
/// Strongly typed identifier for a BrandAppearance entity.
/// Backed by a sortable UUIDv7 to ensure database index locality and prevent enumeration attacks.
/// </summary>
public readonly record struct BrandAppearanceId
{
    public Guid Value { get; }

    public BrandAppearanceId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("BrandAppearanceId cannot be an empty Guid.");
        }

        Value = value;
    }

    public static BrandAppearanceId New() => new(Guid.CreateVersion7());

    public static BrandAppearanceId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(BrandAppearanceId id) => id.Value;
}
