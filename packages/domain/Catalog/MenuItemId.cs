using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Strongly-typed identifier for a MenuItem aggregate root.
/// </summary>
public readonly record struct MenuItemId : IComparable<MenuItemId>
{
    public Guid Value { get; }

    public MenuItemId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("MenuItemId cannot be empty.");
        }

        Value = value;
    }

    public static MenuItemId New() => new(Guid.NewGuid());

    public static MenuItemId From(Guid value) => new(value);

    public int CompareTo(MenuItemId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(MenuItemId id) => id.Value;
}
