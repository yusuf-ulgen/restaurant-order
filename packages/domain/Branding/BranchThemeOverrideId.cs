using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branding;

/// <summary>
/// Strongly typed identifier for a BranchThemeOverride entity.
/// Backed by a sortable UUIDv7 to ensure database index locality and prevent enumeration attacks.
/// </summary>
public readonly record struct BranchThemeOverrideId
{
    public Guid Value { get; }

    public BranchThemeOverrideId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("BranchThemeOverrideId cannot be an empty Guid.");
        }

        Value = value;
    }

    public static BranchThemeOverrideId New() => new(Guid.CreateVersion7());

    public static BranchThemeOverrideId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(BranchThemeOverrideId id) => id.Value;
}
