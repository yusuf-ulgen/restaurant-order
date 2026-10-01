using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Validates and parses 256-bit (32-byte) AES cryptographic keys across startup configuration
/// and outbox payload protectors.
/// Enforces consistent length and format semantics across all environments.
/// </summary>
public static class AesKeyValidator
{
    public const int RequiredKeyByteSize = 32;

    public static bool TryParseKey(
        string? rawKey,
        [NotNullWhen(true)] out byte[]? keyBytes,
        [NotNullWhen(false)] out string? errorMessage)
    {
        keyBytes = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawKey))
        {
            errorMessage = "Encryption key cannot be empty or whitespace.";
            return false;
        }

        var trimmed = rawKey.Trim();

        // 1. 64 hex characters (256-bit representation)
        if (trimmed.Length == 64 && IsHexString(trimmed))
        {
            try
            {
                keyBytes = Convert.FromHexString(trimmed);
                return true;
            }
            catch (FormatException)
            {
                // Fall through if hex parse fails
            }
        }

        // 2. Base64 encoding decoding to exactly 32 bytes
        if (trimmed.Length >= 43 && trimmed.Length <= 45 && (trimmed.EndsWith('=') || trimmed.Length == 43 || trimmed.Length == 44))
        {
            try
            {
                var decoded = Convert.FromBase64String(trimmed);
                if (decoded.Length == RequiredKeyByteSize)
                {
                    keyBytes = decoded;
                    return true;
                }
            }
            catch (FormatException)
            {
                // Not valid base64
            }
        }

        // 3. Exactly 32 UTF-8 encoded bytes
        var utf8ByteCount = Encoding.UTF8.GetByteCount(trimmed);
        if (utf8ByteCount == RequiredKeyByteSize)
        {
            keyBytes = Encoding.UTF8.GetBytes(trimmed);
            return true;
        }

        errorMessage = $"NOTIFICATION_ENCRYPTION_KEY must represent a valid 256-bit (32 bytes) key as exactly 64 hex characters, valid 32-byte Base64, or exactly 32 UTF-8 bytes. Input length: {trimmed.Length} characters, {utf8ByteCount} UTF-8 bytes.";
        return false;
    }

    public static byte[] ParseKey(string rawKey)
    {
        if (!TryParseKey(rawKey, out var keyBytes, out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return keyBytes;
    }

    private static bool IsHexString(string str)
    {
        foreach (var c in str)
        {
            var isHex = (c >= '0' && c <= '9') ||
                        (c >= 'a' && c <= 'f') ||
                        (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }
}
