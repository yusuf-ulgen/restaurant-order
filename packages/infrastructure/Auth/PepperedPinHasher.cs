using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RestaurantOrder.Application.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Salted, high-iteration slow-hash PIN hasher with server-side pepper injection.
/// 4-digit PINs are low entropy (10,000 combinations) and require PBKDF2 stretching
/// combined with a secret pepper to prevent dictionary and offline brute-force attacks.
/// </summary>
public sealed class PepperedPinHasher : IPinHasher
{
    public const string AlgorithmVersion = "pbkdf2_sha512_v1";
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int IterationCount = 100_000;

    private readonly PinHasherOptions _options;

    public PepperedPinHasher(IOptions<PinHasherOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public PinHashResult HashPin(string raw4DigitPin)
    {
        ValidatePinFormat(raw4DigitPin);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var subKey = ComputeHash(raw4DigitPin, salt, _options.PepperValue);

        var hashString = $"{AlgorithmVersion}${Convert.ToHexString(salt)}${Convert.ToHexString(subKey)}";
        return new PinHashResult(hashString, AlgorithmVersion, _options.PepperKeyId);
    }

    public PinVerificationResult VerifyPin(string raw4DigitPin, string storedHash, string storedPepperKeyId)
    {
        if (string.IsNullOrWhiteSpace(raw4DigitPin) || string.IsNullOrWhiteSpace(storedHash))
        {
            return PinVerificationResult.Failed;
        }

        if (raw4DigitPin.Length != 4 || !raw4DigitPin.All(char.IsDigit))
        {
            return PinVerificationResult.Failed;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 3 || parts[0] != AlgorithmVersion)
        {
            return PinVerificationResult.Failed;
        }

        byte[] salt;
        byte[] expectedSubKey;
        try
        {
            salt = Convert.FromHexString(parts[1]);
            expectedSubKey = Convert.FromHexString(parts[2]);
        }
        catch (FormatException)
        {
            return PinVerificationResult.Failed;
        }

        var actualSubKey = ComputeHash(raw4DigitPin, salt, _options.PepperValue);

        if (!CryptographicOperations.FixedTimeEquals(actualSubKey, expectedSubKey))
        {
            return PinVerificationResult.Failed;
        }

        if (!string.Equals(storedPepperKeyId, _options.PepperKeyId, StringComparison.Ordinal))
        {
            return PinVerificationResult.SuccessRehashNeeded;
        }

        return PinVerificationResult.Success;
    }

    private static void ValidatePinFormat(string rawPin)
    {
        if (string.IsNullOrWhiteSpace(rawPin) || rawPin.Length != 4 || !rawPin.All(char.IsDigit))
        {
            throw new ArgumentException("PIN must be exactly 4 numeric digits.", nameof(rawPin));
        }
    }

    private static byte[] ComputeHash(string rawPin, byte[] salt, string pepper)
    {
        var inputBytes = Encoding.UTF8.GetBytes($"{rawPin}:{pepper}");
        return Rfc2898DeriveBytes.Pbkdf2(
            inputBytes,
            salt,
            IterationCount,
            HashAlgorithmName.SHA512,
            HashSizeBytes);
    }
}
