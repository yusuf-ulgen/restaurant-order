using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Orders;

/// <summary>
/// Strongly-typed identifier for an OrderItem entity.
/// </summary>
public readonly record struct OrderItemId : IComparable<OrderItemId>
{
    public Guid Value { get; }

    public OrderItemId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("OrderItemId cannot be empty.");
        }

        Value = value;
    }

    public static OrderItemId New() => new(Guid.NewGuid());
    public static OrderItemId From(Guid value) => new(value);

    public int CompareTo(OrderItemId other) => Value.CompareTo(other.Value);
    public override string ToString() => Value.ToString();

    public static implicit operator Guid(OrderItemId id) => id.Value;
}
