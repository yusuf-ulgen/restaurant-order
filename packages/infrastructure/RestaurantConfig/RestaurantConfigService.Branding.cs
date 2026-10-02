using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branding;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.RestaurantConfig;

public partial class RestaurantConfigService
{
    public async Task<BrandThemeDto?> GetBrandThemeAsync(TenantId tenantId, BrandId brandId, CancellationToken ct = default)
    {
        var appearance = await _dbContext.BrandAppearances
            .AsNoTracking()
            .FirstOrDefaultAsync(ba => ba.TenantId == tenantId && ba.BrandId == brandId, ct);

        if (appearance == null)
        {
            var brandExists = await _dbContext.Brands
                .AnyAsync(b => b.TenantId == tenantId && b.Id == brandId, ct);
            if (!brandExists)
            {
                return null;
            }

            // Return default brand theme
            return CreateDefaultBrandThemeDto(tenantId, brandId);
        }

        return MapBrandTheme(appearance);
    }

    public async Task<BrandThemeDto> UpdateBrandThemeAsync(
        TenantId tenantId,
        BrandId brandId,
        UpdateBrandThemeCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var brand = await _dbContext.Brands
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == brandId, ct)
            ?? throw new ResourceNotFoundException($"Brand with ID '{brandId.Value}' was not found.");

        var appearance = await _dbContext.BrandAppearances
            .FirstOrDefaultAsync(ba => ba.TenantId == tenantId && ba.BrandId == brandId, ct);

        if (appearance == null)
        {
            appearance = BrandAppearance.Create(
                tenantId: tenantId,
                brandId: brandId,
                displayName: command.DisplayName);

            appearance.UpdateDetails(
                displayName: command.DisplayName,
                logoUrl: command.LogoUrl,
                faviconUrl: command.FaviconUrl,
                primaryColor: command.PrimaryColor,
                primaryHoverColor: command.PrimaryHoverColor,
                secondaryColor: command.SecondaryColor,
                accentColor: command.AccentColor,
                surfaceColor: command.SurfaceColor,
                backgroundColor: command.BackgroundColor,
                footerText: command.FooterText,
                defaultShellTitle: command.DefaultShellTitle,
                defaultShellSubtitle: command.DefaultShellSubtitle,
                navigationConfigJson: command.NavigationConfigJson);

            _dbContext.BrandAppearances.Add(appearance);
        }
        else
        {
            VerifyConcurrencyToken(appearance.ConcurrencyToken, command.ConcurrencyToken);

            appearance.UpdateDetails(
                displayName: command.DisplayName,
                logoUrl: command.LogoUrl,
                faviconUrl: command.FaviconUrl,
                primaryColor: command.PrimaryColor,
                primaryHoverColor: command.PrimaryHoverColor,
                secondaryColor: command.SecondaryColor,
                accentColor: command.AccentColor,
                surfaceColor: command.SurfaceColor,
                backgroundColor: command.BackgroundColor,
                footerText: command.FooterText,
                defaultShellTitle: command.DefaultShellTitle,
                defaultShellSubtitle: command.DefaultShellSubtitle,
                navigationConfigJson: command.NavigationConfigJson);
        }

        AddAuditEvent(
            tenantId: tenantId,
            eventType: SecurityAuditEventType.BrandThemeUpdated,
            actor: actor,
            details: new
            {
                BrandId = brandId.Value,
                BrandAppearanceId = appearance.Id.Value,
                DisplayName = appearance.DisplayName,
                PrimaryColor = appearance.PrimaryColor.Value
            });

        await _dbContext.SaveChangesAsync(ct);
        return MapBrandTheme(appearance);
    }

    public async Task<EffectiveThemeDto?> GetEffectiveBranchThemeAsync(
        TenantId tenantId,
        BranchId branchId,
        CancellationToken ct = default)
    {
        var branch = await _dbContext.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct);

        if (branch == null)
        {
            return null;
        }

        var brand = await _dbContext.Brands
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == branch.BrandId, ct);

        var brandAppearance = await _dbContext.BrandAppearances
            .AsNoTracking()
            .FirstOrDefaultAsync(ba => ba.TenantId == tenantId && ba.BrandId == branch.BrandId, ct);

        var branchOverride = await _dbContext.BranchThemeOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(bto => bto.TenantId == tenantId && bto.BranchId == branchId, ct);

        return ResolveEffectiveTheme(branch, brand, brandAppearance, branchOverride);
    }

    public async Task<BranchThemeOverrideDto> UpdateBranchThemeOverrideAsync(
        TenantId tenantId,
        BranchId branchId,
        UpdateBranchThemeOverrideCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await _dbContext.Branches
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct)
            ?? throw new ResourceNotFoundException($"Branch with ID '{branchId.Value}' was not found.");

        var branchOverride = await _dbContext.BranchThemeOverrides
            .FirstOrDefaultAsync(bto => bto.TenantId == tenantId && bto.BranchId == branchId, ct);

        if (branchOverride == null)
        {
            branchOverride = BranchThemeOverride.Create(
                tenantId: tenantId,
                branchId: branchId);

            branchOverride.UpdateDetails(
                displayName: command.DisplayName,
                logoUrl: command.LogoUrl,
                headerSubtitle: command.HeaderSubtitle,
                footerBranchInfo: command.FooterBranchInfo);

            _dbContext.BranchThemeOverrides.Add(branchOverride);
        }
        else
        {
            VerifyConcurrencyToken(branchOverride.ConcurrencyToken, command.ConcurrencyToken);

            branchOverride.UpdateDetails(
                displayName: command.DisplayName,
                logoUrl: command.LogoUrl,
                headerSubtitle: command.HeaderSubtitle,
                footerBranchInfo: command.FooterBranchInfo);
        }

        AddAuditEvent(
            tenantId: tenantId,
            eventType: SecurityAuditEventType.BranchThemeOverrideUpdated,
            actor: actor,
            branchId: branchId,
            details: new
            {
                BranchId = branchId.Value,
                OverrideId = branchOverride.Id.Value,
                DisplayName = branchOverride.DisplayName,
                HeaderSubtitle = branchOverride.HeaderSubtitle
            });

        await _dbContext.SaveChangesAsync(ct);
        return MapBranchThemeOverride(branchOverride);
    }

    public async Task<EffectiveThemeDto> ClearBranchThemeOverrideAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await _dbContext.Branches
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct)
            ?? throw new ResourceNotFoundException($"Branch with ID '{branchId.Value}' was not found.");

        var branchOverride = await _dbContext.BranchThemeOverrides
            .FirstOrDefaultAsync(bto => bto.TenantId == tenantId && bto.BranchId == branchId, ct);

        if (branchOverride != null)
        {
            _dbContext.BranchThemeOverrides.Remove(branchOverride);

            AddAuditEvent(
                tenantId: tenantId,
                eventType: SecurityAuditEventType.BranchThemeOverrideCleared,
                actor: actor,
                branchId: branchId,
                details: new
                {
                    BranchId = branchId.Value,
                    OverrideId = branchOverride.Id.Value
                });

            await _dbContext.SaveChangesAsync(ct);
        }

        var brand = await _dbContext.Brands
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == branch.BrandId, ct);

        var brandAppearance = await _dbContext.BrandAppearances
            .AsNoTracking()
            .FirstOrDefaultAsync(ba => ba.TenantId == tenantId && ba.BrandId == branch.BrandId, ct);

        return ResolveEffectiveTheme(branch, brand, brandAppearance, null);
    }

    private static BrandThemeDto CreateDefaultBrandThemeDto(TenantId tenantId, BrandId brandId) => new(
        Id: Guid.Empty,
        TenantId: tenantId.Value,
        BrandId: brandId.Value,
        DisplayName: string.Empty,
        LogoUrl: null,
        FaviconUrl: null,
        PrimaryColor: BrandAppearance.DefaultPrimaryColor.Value,
        PrimaryHoverColor: BrandAppearance.DefaultPrimaryHoverColor.Value,
        SecondaryColor: BrandAppearance.DefaultSecondaryColor.Value,
        AccentColor: BrandAppearance.DefaultAccentColor.Value,
        SurfaceColor: BrandAppearance.DefaultSurfaceColor.Value,
        BackgroundColor: BrandAppearance.DefaultBackgroundColor.Value,
        FooterText: null,
        DefaultShellTitle: null,
        DefaultShellSubtitle: null,
        NavigationConfigJson: null,
        CreatedAtUtc: DateTimeOffset.UnixEpoch.UtcDateTime,
        UpdatedAtUtc: null,
        ConcurrencyToken: Guid.Empty);

    private static BrandThemeDto MapBrandTheme(BrandAppearance ba) => new(
        Id: ba.Id.Value,
        TenantId: ba.TenantId.Value,
        BrandId: ba.BrandId.Value,
        DisplayName: ba.DisplayName,
        LogoUrl: ba.LogoUrl?.Value,
        FaviconUrl: ba.FaviconUrl?.Value,
        PrimaryColor: ba.PrimaryColor.Value,
        PrimaryHoverColor: ba.PrimaryHoverColor.Value,
        SecondaryColor: ba.SecondaryColor.Value,
        AccentColor: ba.AccentColor.Value,
        SurfaceColor: ba.SurfaceColor.Value,
        BackgroundColor: ba.BackgroundColor.Value,
        FooterText: ba.FooterText,
        DefaultShellTitle: ba.DefaultShellTitle,
        DefaultShellSubtitle: ba.DefaultShellSubtitle,
        NavigationConfigJson: ba.NavigationConfigJson,
        CreatedAtUtc: ba.CreatedAtUtc,
        UpdatedAtUtc: ba.UpdatedAtUtc,
        ConcurrencyToken: ba.ConcurrencyToken);

    private static BranchThemeOverrideDto MapBranchThemeOverride(BranchThemeOverride bto) => new(
        Id: bto.Id.Value,
        TenantId: bto.TenantId.Value,
        BranchId: bto.BranchId.Value,
        DisplayName: bto.DisplayName,
        LogoUrl: bto.LogoUrl?.Value,
        HeaderSubtitle: bto.HeaderSubtitle,
        FooterBranchInfo: bto.FooterBranchInfo,
        CreatedAtUtc: bto.CreatedAtUtc,
        UpdatedAtUtc: bto.UpdatedAtUtc,
        ConcurrencyToken: bto.ConcurrencyToken);

    private static EffectiveThemeDto ResolveEffectiveTheme(
        Branch branch,
        Brand? brand,
        BrandAppearance? appearance,
        BranchThemeOverride? branchOverride)
    {
        var brandDisplayName = appearance != null && !string.IsNullOrWhiteSpace(appearance.DisplayName)
            ? appearance.DisplayName
            : brand?.Name ?? branch.Name;

        var branchDisplayName = branchOverride != null && !string.IsNullOrWhiteSpace(branchOverride.DisplayName)
            ? branchOverride.DisplayName
            : brandDisplayName;

        var logoUrl = branchOverride?.LogoUrl != null && !string.IsNullOrWhiteSpace(branchOverride.LogoUrl.Value)
            ? branchOverride.LogoUrl.Value
            : appearance?.LogoUrl?.Value;

        var faviconUrl = appearance?.FaviconUrl?.Value;

        var primaryColor = appearance?.PrimaryColor.Value ?? BrandAppearance.DefaultPrimaryColor.Value;
        var primaryHoverColor = appearance?.PrimaryHoverColor.Value ?? BrandAppearance.DefaultPrimaryHoverColor.Value;
        var secondaryColor = appearance?.SecondaryColor.Value ?? BrandAppearance.DefaultSecondaryColor.Value;
        var accentColor = appearance?.AccentColor.Value ?? BrandAppearance.DefaultAccentColor.Value;
        var surfaceColor = appearance?.SurfaceColor.Value ?? BrandAppearance.DefaultSurfaceColor.Value;
        var backgroundColor = appearance?.BackgroundColor.Value ?? BrandAppearance.DefaultBackgroundColor.Value;

        var shellTitle = appearance?.DefaultShellTitle ?? brandDisplayName;
        var shellSubtitle = branchOverride != null && !string.IsNullOrWhiteSpace(branchOverride.HeaderSubtitle)
            ? branchOverride.HeaderSubtitle
            : appearance?.DefaultShellSubtitle;

        var footerText = appearance?.FooterText;
        var footerBranchInfo = branchOverride?.FooterBranchInfo;

        var hasOverride = branchOverride != null && (
            !string.IsNullOrWhiteSpace(branchOverride.DisplayName) ||
            branchOverride.LogoUrl != null ||
            !string.IsNullOrWhiteSpace(branchOverride.HeaderSubtitle) ||
            !string.IsNullOrWhiteSpace(branchOverride.FooterBranchInfo));

        return new EffectiveThemeDto(
            TenantId: branch.TenantId.Value,
            BrandId: branch.BrandId.Value,
            BranchId: branch.Id.Value,
            BrandDisplayName: brandDisplayName,
            BranchDisplayName: branchDisplayName,
            LogoUrl: logoUrl,
            FaviconUrl: faviconUrl,
            PrimaryColor: primaryColor,
            PrimaryHoverColor: primaryHoverColor,
            SecondaryColor: secondaryColor,
            AccentColor: accentColor,
            SurfaceColor: surfaceColor,
            BackgroundColor: backgroundColor,
            ShellTitle: shellTitle,
            ShellSubtitle: shellSubtitle,
            FooterText: footerText,
            FooterBranchInfo: footerBranchInfo,
            HasBranchOverride: hasOverride,
            NavigationConfigJson: appearance?.NavigationConfigJson);
    }
}
