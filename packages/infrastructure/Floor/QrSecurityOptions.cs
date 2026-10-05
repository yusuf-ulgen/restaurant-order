namespace RestaurantOrder.Infrastructure.Floor;

/// <summary>
/// Configuration options for table QR cryptographic signing, verification, and rotation.
/// </summary>
public sealed class QrSecurityOptions
{
    public const string SectionName = "QrSecurity";

    public string CurrentKeyId { get; set; } = "k1";

    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);

    public int DynamicQrLifetimeMinutes { get; set; } = 30;

    public int ClockSkewSeconds { get; set; } = 30;
}
