using Microsoft.AspNetCore.Identity;
using RestaurantOrder.Application.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Password hasher implementation leveraging ASP.NET Core's battle-tested PasswordHasher primitive
/// (PBKDF2 with HMAC-SHA512, 100,000 iterations, format v3).
/// Never stores or logs plain-text passwords.
/// </summary>
public sealed class AspNetCorePasswordHasher : IPasswordHasher
{
    private static readonly object DummyUser = new();
    private readonly PasswordHasher<object> _hasher = new();
    public const string CurrentAlgorithmVersion = "identity_v3_pbkdf2";

    public PasswordHashResult HashPassword(string rawPassword)
    {
        if (string.IsNullOrWhiteSpace(rawPassword))
        {
            throw new ArgumentException("Password cannot be null or empty.", nameof(rawPassword));
        }

        var hash = _hasher.HashPassword(DummyUser, rawPassword);
        return new PasswordHashResult(hash, CurrentAlgorithmVersion);
    }

    public RestaurantOrder.Application.Auth.PasswordVerificationResult VerifyPassword(string rawPassword, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(rawPassword) || string.IsNullOrWhiteSpace(storedHash))
        {
            return RestaurantOrder.Application.Auth.PasswordVerificationResult.Failed;
        }

        try
        {
            var result = _hasher.VerifyHashedPassword(DummyUser, storedHash, rawPassword);
            return result switch
            {
                Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success =>
                    RestaurantOrder.Application.Auth.PasswordVerificationResult.Success,
                Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded =>
                    RestaurantOrder.Application.Auth.PasswordVerificationResult.SuccessRehashNeeded,
                _ => RestaurantOrder.Application.Auth.PasswordVerificationResult.Failed
            };
        }
        catch (FormatException)
        {
            return RestaurantOrder.Application.Auth.PasswordVerificationResult.Failed;
        }
    }
}
