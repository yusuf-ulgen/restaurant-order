using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Strongly typed identifier for a physical restaurant Branch.
/// Backed by a sortable UUIDv7 to ensure database index locality and prevent enumeration attacks.
/// </summary>
public readonly record struct BranchId
{
    public Guid Value { get; }

    public BranchId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("BranchId cannot be an empty Guid.");
        }

        Value = value;
    }

    public static BranchId New() => new(Guid.CreateVersion7());

    public static BranchId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(BranchId id) => id.Value;
}
