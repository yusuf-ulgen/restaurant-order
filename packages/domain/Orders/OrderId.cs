using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Orders;

/// <summary>
/// Strongly-typed identifier for an Order aggregate root.
/// </summary>
public readonly record struct OrderId : IComparable<OrderId>
{
    public Guid Value { get; }

    public OrderId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("OrderId cannot be empty.");
        }

        Value = value;
    }

    public static OrderId New() => new(Guid.NewGuid());
    public static OrderId From(Guid value) => new(value);

    public int CompareTo(OrderId other) => Value.CompareTo(other.Value);
    public override string ToString() => Value.ToString();

    public static implicit operator Guid(OrderId id) => id.Value;
}
