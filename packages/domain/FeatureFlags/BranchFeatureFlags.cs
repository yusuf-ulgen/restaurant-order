using System.Text.Json;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.FeatureFlags;

/// <summary>
/// Branch-specific feature flag overrides aggregate root.
/// Allows fine-grained feature enablement/disablement per branch on top of tenant defaults.
/// </summary>
public class BranchFeatureFlags
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public string OverridesJson { get; private set; } = "{}";
    public Guid ConcurrencyToken { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private BranchFeatureFlags()
    {
    }

    public static BranchFeatureFlags Create(
        TenantId tenantId,
        BranchId branchId,
        IDictionary<string, bool>? initialOverrides = null)
    {
        var overrides = initialOverrides != null
            ? SanitizeAndValidate(initialOverrides)
            : new Dictionary<string, bool>();

        return new BranchFeatureFlags
        {
            TenantId = tenantId,
            BranchId = branchId,
            OverridesJson = JsonSerializer.Serialize(overrides, JsonOptions),
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Update(IDictionary<string, bool> newOverrides)
    {
        var sanitized = SanitizeAndValidate(newOverrides);
        OverridesJson = JsonSerializer.Serialize(sanitized, JsonOptions);
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyDictionary<string, bool> GetOverrides()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(OverridesJson, JsonOptions)
                ?? new Dictionary<string, bool>();
        }
        catch
        {
            return new Dictionary<string, bool>();
        }
    }

    private static Dictionary<string, bool> SanitizeAndValidate(IDictionary<string, bool> overrides)
    {
        var sanitized = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (key, value) in overrides)
        {
            FeatureFlagKey.ValidateKey(key);
            sanitized[key] = value;
        }

        return sanitized;
    }
}
