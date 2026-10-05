using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Catalog;

/// <summary>
/// Application service contract for branch item availability and Quick 86 operations.
/// </summary>
public interface ICatalogAvailabilityService
{
    Task<IReadOnlyList<BranchItemAvailabilityDto>> ListBranchAvailabilitiesAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<BranchItemAvailabilityDto> Quick86ItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        Quick86ItemCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<BranchItemAvailabilityDto> RestockItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        RestockItemCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<BranchItemAvailabilityDto> Quick86VariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        Quick86VariantCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<BranchItemAvailabilityDto> RestockVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        RestockVariantCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<RuntimeMenuReadModel> GetRuntimeMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);
}
