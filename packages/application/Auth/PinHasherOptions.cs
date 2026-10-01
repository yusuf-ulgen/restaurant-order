namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Options for configuring the salted and peppered PIN hasher.
/// Enforces fail-fast configuration checks for production and staging environments.
/// </summary>
public sealed class PinHasherOptions
{
    public const string DefaultSectionName = "Auth:PinHasher";

    /// <summary>Active pepper version key identifier (e.g., "v1").</summary>
    public string PepperKeyId { get; set; } = "v1";

    /// <summary>Server-side secret pepper string injected from environment/secret manager.</summary>
    public string PepperValue { get; set; } = string.Empty;

    /// <summary>Host environment name (e.g., "production", "staging", "development", "test").</summary>
    public string Environment { get; set; } = "development";

    /// <summary>
    /// Validates that required pepper secrets are configured.
    /// Strictly fails fast in production and staging if the pepper is missing or weak.
    /// </summary>
    public void Validate()
    {
        var isProductionOrStaging = string.Equals(Environment, "production", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment, "staging", StringComparison.OrdinalIgnoreCase);

        if (isProductionOrStaging)
        {
            if (string.IsNullOrWhiteSpace(PepperValue) || PepperValue.Length < 16)
            {
                throw new InvalidOperationException(
                    "CRITICAL SECURITY CONFIGURATION ERROR: PIN pepper secret (PepperValue) must be non-empty " +
                    "and at least 16 characters in production and staging environments.");
            }
        }
    }
}
