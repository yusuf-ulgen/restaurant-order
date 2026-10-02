namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Contract for cryptographically secure, battle-tested password hashing and verification.
/// Must never store or log raw passwords.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Computes a secure cryptographic hash for the given plain-text password.
    /// </summary>
    PasswordHashResult HashPassword(string rawPassword);

    /// <summary>
    /// Verifies a plain-text password against a stored cryptographic hash.
    /// </summary>
    PasswordVerificationResult VerifyPassword(string rawPassword, string storedHash);
}
