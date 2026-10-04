using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Strongly-typed identifier for a Menu aggregate root.
/// </summary>
public readonly record struct MenuId : IComparable<MenuId>
{
    public Guid Value { get; }

    public MenuId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("MenuId cannot be empty.");
        }

        Value = value;
    }

    public static MenuId New() => new(Guid.NewGuid());

    public static MenuId From(Guid value) => new(value);

    public int CompareTo(MenuId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(MenuId id) => id.Value;
}
