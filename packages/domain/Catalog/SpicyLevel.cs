using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Closed value object representing spice intensity level from 0 (Not Spicy) to 3 (Extra Hot).
/// </summary>
public readonly record struct SpicyLevel : IComparable<SpicyLevel>
{
    public const int MinValue = 0;
    public const int MaxValue = 3;

    public int Value { get; }

    public SpicyLevel(int value)
    {
        if (value is < MinValue or > MaxValue)
        {
            throw new DomainException($"SpicyLevel must be between {MinValue} and {MaxValue}. Provided: {value}.");
        }

        Value = value;
    }

    public static SpicyLevel None => new(0);
    public static SpicyLevel Mild => new(1);
    public static SpicyLevel Medium => new(2);
    public static SpicyLevel Hot => new(3);

    public static SpicyLevel FromInt(int value) => new(value);

    public int CompareTo(SpicyLevel other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator int(SpicyLevel level) => level.Value;
    public static explicit operator SpicyLevel(int value) => new(value);
}
