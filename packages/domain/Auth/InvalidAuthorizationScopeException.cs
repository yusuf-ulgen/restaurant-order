using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Thrown when an illegal role, scope, or tenant/branch pairing is requested.
/// </summary>
public sealed class InvalidAuthorizationScopeException : DomainException
{
    public InvalidAuthorizationScopeException(string message) : base(message)
    {
    }
}
