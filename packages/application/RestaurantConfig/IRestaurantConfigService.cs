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
}
