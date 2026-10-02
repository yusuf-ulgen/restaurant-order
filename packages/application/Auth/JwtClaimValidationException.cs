namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Thrown when JWT claims fail structure, cryptographic timing, tenancy, or role validation.
/// </summary>
public sealed class JwtClaimValidationException : Exception
{
    public string? ClaimName { get; }

    public JwtClaimValidationException(string message) : base(message)
    {
    }

    public JwtClaimValidationException(string claimName, string message)
        : base($"Claim '{claimName}' validation failed: {message}")
    {
        ClaimName = claimName;
    }
}
