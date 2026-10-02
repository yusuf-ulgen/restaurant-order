using System.Security.Cryptography;
using System.Text;
using RestaurantOrder.Application.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Service producing 256-bit cryptographically random opaque refresh tokens and computing SHA-256 hashes.
/// </summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    public string GenerateOpaqueToken()
    {
        var randomBytes = new byte[32]; // 256 bits of entropy
        RandomNumberGenerator.Fill(randomBytes);
        return Convert.ToHexString(randomBytes).ToLowerInvariant();
    }

    public string HashToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw new ArgumentException("Token cannot be null or empty.", nameof(rawToken));
        }

        var bytes = Encoding.UTF8.GetBytes(rawToken.Trim());
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
