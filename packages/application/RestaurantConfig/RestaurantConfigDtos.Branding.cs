namespace RestaurantOrder.Application.RestaurantConfig;

public sealed record BrandThemeDto(
    Guid Id,
    Guid TenantId,
    Guid BrandId,
    string DisplayName,
    string? LogoUrl,
    string? FaviconUrl,
    string PrimaryColor,
    string PrimaryHoverColor,
    string SecondaryColor,
    string AccentColor,
    string SurfaceColor,
    string BackgroundColor,
    string? FooterText,
    string? DefaultShellTitle,
    string? DefaultShellSubtitle,
    string? NavigationConfigJson,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken);

public sealed record BranchThemeOverrideDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string? DisplayName,
    string? LogoUrl,
    string? HeaderSubtitle,
    string? FooterBranchInfo,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken);

public sealed record NavigationItemOverrideDto(
    string Id,
    bool? IsVisible = null,
    int? Order = null,
    string? LabelOverride = null,
    string? Section = null,
    bool? Disabled = null);

public sealed record EffectiveThemeDto(
    Guid TenantId,
    Guid BrandId,
    Guid BranchId,
    string BrandDisplayName,
    string BranchDisplayName,
    string? LogoUrl,
    string? FaviconUrl,
    string PrimaryColor,
    string PrimaryHoverColor,
    string SecondaryColor,
    string AccentColor,
    string SurfaceColor,
    string BackgroundColor,
    string? ShellTitle,
    string? ShellSubtitle,
    string? FooterText,
    string? FooterBranchInfo,
    bool HasBranchOverride,
    string? NavigationConfigJson = null);

public sealed record UpdateBrandThemeCommand(
    string DisplayName,
    string? LogoUrl = null,
    string? FaviconUrl = null,
    string? PrimaryColor = null,
    string? PrimaryHoverColor = null,
    string? SecondaryColor = null,
    string? AccentColor = null,
    string? SurfaceColor = null,
    string? BackgroundColor = null,
    string? FooterText = null,
    string? DefaultShellTitle = null,
    string? DefaultShellSubtitle = null,
    string? NavigationConfigJson = null,
    Guid? ConcurrencyToken = null);

public sealed record UpdateBranchThemeOverrideCommand(
    string? DisplayName = null,
    string? LogoUrl = null,
    string? HeaderSubtitle = null,
    string? FooterBranchInfo = null,
    Guid? ConcurrencyToken = null);
