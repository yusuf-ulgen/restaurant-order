using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Strongly-typed identifier for a BranchSettings aggregate root.
/// </summary>
public readonly record struct BranchSettingsId : IComparable<BranchSettingsId>
{
    public Guid Value { get; }

    public BranchSettingsId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("BranchSettingsId cannot be empty.");
        }

        Value = value;
    }

    public static BranchSettingsId New() => new(Guid.NewGuid());

    public static BranchSettingsId From(Guid value) => new(value);

    public int CompareTo(BranchSettingsId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(BranchSettingsId id) => id.Value;
}
