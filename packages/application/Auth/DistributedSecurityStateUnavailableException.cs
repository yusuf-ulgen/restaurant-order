namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Thrown when distributed state infrastructure (such as Redis) is unavailable
/// and security controls must fail closed rather than permitting unverified access.
/// </summary>
public class DistributedSecurityStateUnavailableException : Exception
{
    public DistributedSecurityStateUnavailableException(string message)
        : base(message)
    {
    }

    public DistributedSecurityStateUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
