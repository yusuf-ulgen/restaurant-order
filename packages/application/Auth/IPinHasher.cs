namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Contract for salted, slow-hashed, peppered staff PIN operations.
/// 4-digit PINs are low-entropy credentials that require high-iteration stretching
/// combined with a secret server-side pepper to mitigate offline brute-force attacks.
/// </summary>
public interface IPinHasher
{
    /// <summary>
    /// Computes a salted, peppered cryptographic hash for a 4-digit PIN.
    /// </summary>
    PinHashResult HashPin(string raw4DigitPin);

    /// <summary>
    /// Verifies a 4-digit PIN against a stored hash and pepper key identifier.
    /// </summary>
    PinVerificationResult VerifyPin(string raw4DigitPin, string storedHash, string storedPepperKeyId);
}
