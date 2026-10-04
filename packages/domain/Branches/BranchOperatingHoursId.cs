using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Strongly-typed identifier for a BranchOperatingHours aggregate root.
/// </summary>
public readonly record struct BranchOperatingHoursId : IComparable<BranchOperatingHoursId>
{
    public Guid Value { get; }

    public BranchOperatingHoursId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("BranchOperatingHoursId cannot be empty.");
        }

        Value = value;
    }

    public static BranchOperatingHoursId New() => new(Guid.NewGuid());

    public static BranchOperatingHoursId From(Guid value) => new(value);

    public int CompareTo(BranchOperatingHoursId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(BranchOperatingHoursId id) => id.Value;
}
