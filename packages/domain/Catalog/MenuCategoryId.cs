using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Strongly-typed identifier for a MenuCategory entity.
/// </summary>
public readonly record struct MenuCategoryId : IComparable<MenuCategoryId>
{
    public Guid Value { get; }

    public MenuCategoryId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("MenuCategoryId cannot be empty.");
        }

        Value = value;
    }

    public static MenuCategoryId New() => new(Guid.NewGuid());

    public static MenuCategoryId From(Guid value) => new(value);

    public int CompareTo(MenuCategoryId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(MenuCategoryId id) => id.Value;
}
