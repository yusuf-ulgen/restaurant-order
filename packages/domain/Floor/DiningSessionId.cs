using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Strongly-typed identifier for a DiningSession aggregate root.
/// </summary>
public readonly record struct DiningSessionId : IComparable<DiningSessionId>
{
    public Guid Value { get; }

    public DiningSessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("DiningSessionId cannot be empty.");
        }

        Value = value;
    }

    public static DiningSessionId New() => new(Guid.NewGuid());
    public static DiningSessionId From(Guid value) => new(value);

    public int CompareTo(DiningSessionId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(DiningSessionId id) => id.Value;
}
