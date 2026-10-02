namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Configuration settings for JWT generation and verification.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "restaurant-order";
    public string Audience { get; set; } = "restaurant-order-clients";
    public int AccessTokenLifetimeMinutes { get; set; } = 10;
    public string KeyId { get; set; } = "k1";
    public int ClockSkewSeconds { get; set; } = 5;
}
