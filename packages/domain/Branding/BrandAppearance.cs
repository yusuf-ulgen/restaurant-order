using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Branding;

/// <summary>
/// Corporate branding and theme appearance settings for a brand.
/// Controls design tokens, logos, and shell text elements.
/// </summary>
public class BrandAppearance
{
    public static readonly ColorHex DefaultPrimaryColor = new("#111827");
    public static readonly ColorHex DefaultPrimaryHoverColor = new("#1f2937");
    public static readonly ColorHex DefaultSecondaryColor = new("#4b5563");
    public static readonly ColorHex DefaultAccentColor = new("#2563eb");
    public static readonly ColorHex DefaultSurfaceColor = new("#ffffff");
    public static readonly ColorHex DefaultBackgroundColor = new("#f9fafb");

    public BrandAppearanceId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BrandId BrandId { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public AssetUrl? LogoUrl { get; private set; }
    public AssetUrl? FaviconUrl { get; private set; }
    public ColorHex PrimaryColor { get; private set; } = DefaultPrimaryColor;
    public ColorHex PrimaryHoverColor { get; private set; } = DefaultPrimaryHoverColor;
    public ColorHex SecondaryColor { get; private set; } = DefaultSecondaryColor;
    public ColorHex AccentColor { get; private set; } = DefaultAccentColor;
    public ColorHex SurfaceColor { get; private set; } = DefaultSurfaceColor;
    public ColorHex BackgroundColor { get; private set; } = DefaultBackgroundColor;
    public string? FooterText { get; private set; }
    public string? DefaultShellTitle { get; private set; }
    public string? DefaultShellSubtitle { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private BrandAppearance()
    {
    }

    private BrandAppearance(
        BrandAppearanceId id,
        TenantId tenantId,
        BrandId brandId,
        string displayName,
        DateTime createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        BrandId = brandId;
        SetDisplayName(displayName);
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public static BrandAppearance Create(
        TenantId tenantId,
        Brand brand,
        string displayName,
        BrandAppearanceId? id = null)
    {
        if (brand.TenantId != tenantId)
        {
            throw new DomainException(
                $"Tenant mismatch: Brand '{brand.Name}' belongs to tenant '{brand.TenantId}', but appearance was requested for '{tenantId}'.");
        }

        var appearanceId = id ?? BrandAppearanceId.New();
        return new BrandAppearance(appearanceId, tenantId, brand.Id, displayName, DateTime.UtcNow);
    }

    public static BrandAppearance Create(
        TenantId tenantId,
        BrandId brandId,
        string displayName,
        BrandAppearanceId? id = null)
    {
        var appearanceId = id ?? BrandAppearanceId.New();
        return new BrandAppearance(appearanceId, tenantId, brandId, displayName, DateTime.UtcNow);
    }

    public void UpdateDetails(
        string displayName,
        string? logoUrl,
        string? faviconUrl,
        string? primaryColor,
        string? primaryHoverColor,
        string? secondaryColor,
        string? accentColor,
        string? surfaceColor,
        string? backgroundColor,
        string? footerText,
        string? defaultShellTitle,
        string? defaultShellSubtitle)
    {
        SetDisplayName(displayName);
        LogoUrl = AssetUrl.FromNullable(logoUrl);
        FaviconUrl = AssetUrl.FromNullable(faviconUrl);
        PrimaryColor = ColorHex.FromNullable(primaryColor) ?? DefaultPrimaryColor;
        PrimaryHoverColor = ColorHex.FromNullable(primaryHoverColor) ?? DefaultPrimaryHoverColor;
        SecondaryColor = ColorHex.FromNullable(secondaryColor) ?? DefaultSecondaryColor;
        AccentColor = ColorHex.FromNullable(accentColor) ?? DefaultAccentColor;
        SurfaceColor = ColorHex.FromNullable(surfaceColor) ?? DefaultSurfaceColor;
        BackgroundColor = ColorHex.FromNullable(backgroundColor) ?? DefaultBackgroundColor;
        FooterText = SanitizeSafeText(footerText, nameof(FooterText), maxLength: 500);
        DefaultShellTitle = SanitizeSafeText(defaultShellTitle, nameof(DefaultShellTitle), maxLength: 100);
        DefaultShellSubtitle = SanitizeSafeText(defaultShellSubtitle, nameof(DefaultShellSubtitle), maxLength: 200);

        Touch();
    }

    private void SetDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("Display name cannot be null, empty, or whitespace.");
        }

        var trimmed = displayName.Trim();
        if (trimmed.Length > 100)
        {
            throw new DomainException("Display name cannot exceed 100 characters.");
        }

        AssertNoHtml(trimmed, nameof(DisplayName));
        DisplayName = trimmed;
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

        AssertNoHtml(trimmed, fieldName);
        return trimmed;
    }

    private static void AssertNoHtml(string value, string fieldName)
    {
        if (value.Contains('<') || value.Contains('>'))
        {
            throw new DomainException($"{fieldName} contains disallowed characters or HTML tags.");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }
}
