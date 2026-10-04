namespace RestaurantOrder.Domain.Catalog;

public readonly record struct ModifierGroupId(Guid Value)
{
    public static ModifierGroupId New() => new(Guid.NewGuid());
    public static implicit operator Guid(ModifierGroupId id) => id.Value;
    public static explicit operator ModifierGroupId(Guid id) => new(id);
    public override string ToString() => Value.ToString();
}

public readonly record struct ModifierOptionId(Guid Value)
{
    public static ModifierOptionId New() => new(Guid.NewGuid());
    public static implicit operator Guid(ModifierOptionId id) => id.Value;
    public static explicit operator ModifierOptionId(Guid id) => new(id);
    public override string ToString() => Value.ToString();
}
