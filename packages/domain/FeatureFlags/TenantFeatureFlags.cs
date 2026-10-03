using System.Text.Json;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.FeatureFlags;

/// <summary>
/// Tenant-level default feature flag settings aggregate root.
/// Defines baseline feature toggles applied across all branches under the organization.
/// </summary>
public class TenantFeatureFlags
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TenantId TenantId { get; private set; }
    public string FlagsJson { get; private set; } = "{}";
    public Guid ConcurrencyToken { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private TenantFeatureFlags()
    {
    }

    public static TenantFeatureFlags Create(
        TenantId tenantId,
        IDictionary<string, bool>? initialFlags = null)
    {
        var flags = initialFlags != null ? SanitizeAndValidate(initialFlags) : new Dictionary<string, bool>();

        return new TenantFeatureFlags
        {
            TenantId = tenantId,
            FlagsJson = JsonSerializer.Serialize(flags, JsonOptions),
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Update(IDictionary<string, bool> newFlags)
    {
        var sanitized = SanitizeAndValidate(newFlags);
        FlagsJson = JsonSerializer.Serialize(sanitized, JsonOptions);
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyDictionary<string, bool> GetFlags()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(FlagsJson, JsonOptions)
                ?? new Dictionary<string, bool>();
        }
        catch
        {
            return new Dictionary<string, bool>();
        }
    }

    private static Dictionary<string, bool> SanitizeAndValidate(IDictionary<string, bool> flags)
    {
        var sanitized = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (key, value) in flags)
        {
            FeatureFlagKey.ValidateKey(key);
            sanitized[key] = value;
        }

        return sanitized;
    }
}
