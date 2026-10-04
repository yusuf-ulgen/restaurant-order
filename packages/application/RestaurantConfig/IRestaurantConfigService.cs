using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.RestaurantConfig;

public interface IRestaurantConfigService
{
    // Brand operations
    Task<IReadOnlyList<BrandDto>> ListBrandsAsync(TenantId tenantId, CancellationToken ct = default);
    Task<BrandDto?> GetBrandByIdAsync(TenantId tenantId, BrandId brandId, CancellationToken ct = default);
    Task<BrandDto> CreateBrandAsync(TenantId tenantId, CreateBrandCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BrandDto> UpdateBrandAsync(TenantId tenantId, BrandId brandId, UpdateBrandCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BrandDto> ActivateBrandAsync(TenantId tenantId, BrandId brandId, BrandStateChangeCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BrandDto> DeactivateBrandAsync(TenantId tenantId, BrandId brandId, BrandStateChangeCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);

    // Branch operations
    Task<IReadOnlyList<BranchDto>> ListBranchesAsync(TenantId tenantId, AuthenticatedPrincipal actor, BrandId? filterBrandId = null, CancellationToken ct = default);
    Task<BranchDto?> GetBranchByIdAsync(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BranchDto> CreateBranchAsync(TenantId tenantId, CreateBranchCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BranchDto> UpdateBranchAsync(TenantId tenantId, BranchId branchId, UpdateBranchCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BranchDto> ActivateBranchAsync(TenantId tenantId, BranchId branchId, BranchStateChangeCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BranchDto> SuspendBranchAsync(TenantId tenantId, BranchId branchId, BranchStateChangeCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BranchDto> CloseBranchAsync(TenantId tenantId, BranchId branchId, BranchStateChangeCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);

    // Branding and Theme operations
    Task<BrandThemeDto?> GetBrandThemeAsync(TenantId tenantId, BrandId brandId, CancellationToken ct = default);
    Task<BrandThemeDto> UpdateBrandThemeAsync(TenantId tenantId, BrandId brandId, UpdateBrandThemeCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<EffectiveThemeDto?> GetEffectiveBranchThemeAsync(TenantId tenantId, BranchId branchId, CancellationToken ct = default);
    Task<BranchThemeOverrideDto> UpdateBranchThemeOverrideAsync(TenantId tenantId, BranchId branchId, UpdateBranchThemeOverrideCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<EffectiveThemeDto> ClearBranchThemeOverrideAsync(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor, CancellationToken ct = default);

    // Branch Settings & Operating Hours operations (Phase 4.4)
    Task<EffectiveBranchSettingsDto> GetEffectiveBranchSettingsAsync(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BranchSettingsDto> UpdateBranchSettingsAsync(TenantId tenantId, BranchId branchId, UpdateBranchSettingsCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BranchOperatingHoursDto> GetBranchOperatingHoursAsync(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BranchOperatingHoursDto> UpdateBranchOperatingHoursAsync(TenantId tenantId, BranchId branchId, UpdateBranchOperatingHoursCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);

    // Dining Area operations (Phase 4.5)
    Task<IReadOnlyList<DiningAreaDto>> ListDiningAreasAsync(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<DiningAreaDto> CreateDiningAreaAsync(TenantId tenantId, BranchId branchId, CreateDiningAreaCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<DiningAreaDto> UpdateDiningAreaAsync(TenantId tenantId, BranchId branchId, DiningAreaId areaId, UpdateDiningAreaCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<IReadOnlyList<DiningAreaDto>> ReorderDiningAreasAsync(TenantId tenantId, BranchId branchId, ReorderDiningAreasCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<DiningAreaDto> ActivateDiningAreaAsync(TenantId tenantId, BranchId branchId, DiningAreaId areaId, Guid concurrencyToken, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<DiningAreaDto> DeactivateDiningAreaAsync(TenantId tenantId, BranchId branchId, DiningAreaId areaId, Guid concurrencyToken, AuthenticatedPrincipal actor, CancellationToken ct = default);

    // Preparation Station operations (Phase 4.5)
    Task<IReadOnlyList<PreparationStationDto>> ListPreparationStationsAsync(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<IReadOnlyList<StationRuntimeDto>> GetPreparationStationRuntimeAsync(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<PreparationStationDto> CreatePreparationStationAsync(TenantId tenantId, BranchId branchId, CreatePreparationStationCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<PreparationStationDto> UpdatePreparationStationAsync(TenantId tenantId, BranchId branchId, PreparationStationId stationId, UpdatePreparationStationCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<IReadOnlyList<PreparationStationDto>> ReorderPreparationStationsAsync(TenantId tenantId, BranchId branchId, ReorderPreparationStationsCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<PreparationStationDto> ActivatePreparationStationAsync(TenantId tenantId, BranchId branchId, PreparationStationId stationId, Guid concurrencyToken, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<PreparationStationDto> DeactivatePreparationStationAsync(TenantId tenantId, BranchId branchId, PreparationStationId stationId, Guid concurrencyToken, AuthenticatedPrincipal actor, CancellationToken ct = default);

    // Feature Flags operations (Phase 4.5)
    Task<TenantFeatureFlagsDto> GetTenantFeatureFlagsAsync(TenantId tenantId, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<TenantFeatureFlagsDto> UpdateTenantFeatureFlagsAsync(TenantId tenantId, UpdateFeatureFlagsCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BranchFeatureFlagsDto> GetBranchFeatureFlagsOverrideAsync(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<BranchFeatureFlagsDto> UpdateBranchFeatureFlagsOverrideAsync(TenantId tenantId, BranchId branchId, UpdateFeatureFlagsCommand command, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<EffectiveFeatureFlagsDto> ClearBranchFeatureFlagsOverrideAsync(TenantId tenantId, BranchId branchId, Guid concurrencyToken, AuthenticatedPrincipal actor, CancellationToken ct = default);
    Task<EffectiveFeatureFlagsDto> GetEffectiveFeatureFlagsAsync(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor, CancellationToken ct = default);
}
