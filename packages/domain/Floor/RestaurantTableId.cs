using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Strongly-typed identifier for a RestaurantTable aggregate root.
/// </summary>
public readonly record struct RestaurantTableId : IComparable<RestaurantTableId>
{
    public Guid Value { get; }

    public RestaurantTableId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("RestaurantTableId cannot be empty.");
        }

        Value = value;
    }

    public static RestaurantTableId New() => new(Guid.NewGuid());
    public static RestaurantTableId From(Guid value) => new(value);

    public int CompareTo(RestaurantTableId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(RestaurantTableId id) => id.Value;
}
