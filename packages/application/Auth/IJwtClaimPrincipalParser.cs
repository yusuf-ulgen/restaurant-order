using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Contract for validating and parsing raw JWT claim collections into verified AuthenticatedPrincipal instances.
/// Enforces fail-closed token validation, timestamp checks, role-to-scope invariants, and tenancy boundaries.
/// </summary>
public interface IJwtClaimPrincipalParser
{
    /// <summary>
    /// Parses and validates claims from a dictionary.
    /// </summary>
    AuthenticatedPrincipal ParsePrincipal(
        IReadOnlyDictionary<string, string> claims,
        DateTimeOffset? currentTime = null);

    /// <summary>
    /// Parses and validates claims from an enumerable collection of key-value pairs.
    /// Rejects collections containing duplicate distinct keys.
    /// </summary>
    AuthenticatedPrincipal ParsePrincipal(
        IEnumerable<KeyValuePair<string, string>> claims,
        DateTimeOffset? currentTime = null);
}
