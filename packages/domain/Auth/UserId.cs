using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Strongly typed identifier for a User account identity.
/// Backed by a sortable UUIDv7 to ensure database index locality and prevent enumeration attacks.
/// </summary>
public readonly record struct UserId
{
    public Guid Value { get; }

    public UserId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("UserId cannot be an empty Guid.");
        }

        Value = value;
    }

    public static UserId New() => new(Guid.CreateVersion7());

    public static UserId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(UserId id) => id.Value;
}
