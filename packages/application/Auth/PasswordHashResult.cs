namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Encapsulates the output of a password hash operation, including algorithm metadata.
/// </summary>
public sealed record PasswordHashResult(
    string Hash,
    string AlgorithmVersion);
