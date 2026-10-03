using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Strongly-typed identifier for a PreparationStation aggregate root.
/// </summary>
public readonly record struct PreparationStationId : IComparable<PreparationStationId>
{
    public Guid Value { get; }

    public PreparationStationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("PreparationStationId cannot be empty.");
        }

        Value = value;
    }

    public static PreparationStationId New() => new(Guid.NewGuid());

    public static PreparationStationId From(Guid value) => new(value);

    public int CompareTo(PreparationStationId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(PreparationStationId id) => id.Value;
}
