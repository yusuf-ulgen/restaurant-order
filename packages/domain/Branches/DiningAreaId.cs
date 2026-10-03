using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Strongly-typed identifier for a DiningArea aggregate root.
/// </summary>
public readonly record struct DiningAreaId : IComparable<DiningAreaId>
{
    public Guid Value { get; }

    public DiningAreaId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("DiningAreaId cannot be empty.");
        }

        Value = value;
    }

    public static DiningAreaId New() => new(Guid.NewGuid());

    public static DiningAreaId From(Guid value) => new(value);

    public int CompareTo(DiningAreaId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(DiningAreaId id) => id.Value;
}
