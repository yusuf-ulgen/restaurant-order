using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Strongly-typed identifier for an ItemVariant entity.
/// </summary>
public readonly record struct ItemVariantId : IComparable<ItemVariantId>
{
    public Guid Value { get; }

    public ItemVariantId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("ItemVariantId cannot be empty.");
        }

        Value = value;
    }

    public static ItemVariantId New() => new(Guid.NewGuid());

    public static ItemVariantId From(Guid value) => new(value);

    public int CompareTo(ItemVariantId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(ItemVariantId id) => id.Value;
}
