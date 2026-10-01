namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Service for generating cryptographically secure opaque refresh tokens and computing hashes.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Generates a high-entropy cryptographically random opaque refresh token string.
    /// </summary>
    string GenerateOpaqueToken();

    /// <summary>
    /// Computes a deterministic SHA-256 hash of the raw token for safe database persistence.
    /// </summary>
    string HashToken(string rawToken);
}
