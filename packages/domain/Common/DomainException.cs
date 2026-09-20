namespace RestaurantOrder.Domain.Common;

/// <summary>
/// Base exception thrown when a domain invariant or state transition rule is violated.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
