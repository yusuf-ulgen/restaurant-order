using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Branding;

/// <summary>
/// Branch-level branding override.
/// Permits selective overrides for display name, branch logo, header subtitle, and footer branch info.
/// Any null/empty field inherits directly from the parent BrandAppearance.
/// </summary>
public class BranchThemeOverride
{
    public BranchThemeOverrideId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public string? DisplayName { get; private set; }
    public AssetUrl? LogoUrl { get; private set; }
    public string? HeaderSubtitle { get; private set; }
    public string? FooterBranchInfo { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private BranchThemeOverride()
    {
    }

    private BranchThemeOverride(
        BranchThemeOverrideId id,
        TenantId tenantId,
        BranchId branchId,
        DateTime createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        BranchId = branchId;
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public static BranchThemeOverride Create(
        TenantId tenantId,
        Branch branch,
        BranchThemeOverrideId? id = null)
    {
        if (branch.TenantId != tenantId)
        {
            throw new DomainException(
                $"Tenant mismatch: Branch '{branch.Name}' belongs to tenant '{branch.TenantId}', but override was requested for '{tenantId}'.");
        }

        var overrideId = id ?? BranchThemeOverrideId.New();
        return new BranchThemeOverride(overrideId, tenantId, branch.Id, DateTime.UtcNow);
    }

    public static BranchThemeOverride Create(
        TenantId tenantId,
        BranchId branchId,
        BranchThemeOverrideId? id = null)
    {
        var overrideId = id ?? BranchThemeOverrideId.New();
        return new BranchThemeOverride(overrideId, tenantId, branchId, DateTime.UtcNow);
    }

    public void UpdateDetails(
        string? displayName,
        string? logoUrl,
        string? headerSubtitle,
        string? footerBranchInfo)
    {
        DisplayName = SanitizeSafeText(displayName, nameof(DisplayName), maxLength: 100);
        LogoUrl = AssetUrl.FromNullable(logoUrl);
        HeaderSubtitle = SanitizeSafeText(headerSubtitle, nameof(HeaderSubtitle), maxLength: 200);
        FooterBranchInfo = SanitizeSafeText(footerBranchInfo, nameof(FooterBranchInfo), maxLength: 500);

        Touch();
    }

    private static string? SanitizeSafeText(string? text, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{fieldName} cannot exceed {maxLength} characters.");
        }

        if (trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new DomainException($"{fieldName} contains disallowed characters or HTML tags.");
        }

        return trimmed;
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }
}
