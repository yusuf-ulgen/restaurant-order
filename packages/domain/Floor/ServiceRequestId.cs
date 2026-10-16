using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Strongly-typed identifier for a ServiceRequest aggregate root.
/// </summary>
public readonly record struct ServiceRequestId : IComparable<ServiceRequestId>
{
    public Guid Value { get; }

    public ServiceRequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("ServiceRequestId cannot be empty.");
        }

        Value = value;
    }

    public static ServiceRequestId New() => new(Guid.NewGuid());
    public static ServiceRequestId From(Guid value) => new(value);

    public int CompareTo(ServiceRequestId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(ServiceRequestId id) => id.Value;
}
