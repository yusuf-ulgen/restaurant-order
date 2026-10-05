using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Strongly-typed identifier for a BranchItemAvailability record.
/// </summary>
public readonly record struct BranchItemAvailabilityId : IComparable<BranchItemAvailabilityId>
{
    public Guid Value { get; }

    public BranchItemAvailabilityId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("BranchItemAvailabilityId cannot be empty.");
        }

        Value = value;
    }

    public static BranchItemAvailabilityId New() => new(Guid.NewGuid());

    public static BranchItemAvailabilityId From(Guid value) => new(value);

    public int CompareTo(BranchItemAvailabilityId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(BranchItemAvailabilityId id) => id.Value;
}
