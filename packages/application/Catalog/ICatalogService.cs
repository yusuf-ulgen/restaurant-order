using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Catalog;

/// <summary>
/// Application service contract for menu and catalog operations.
/// Enforces tenant boundary isolation, branch-level access control, and optimistic concurrency.
/// </summary>
public interface ICatalogService
{
    // Menus
    Task<IReadOnlyList<MenuDto>> ListMenusAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuDto> GetMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuDto> CreateMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        CreateMenuCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuDto> UpdateMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        UpdateMenuCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuDto> ActivateMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuDto> ArchiveMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    // Categories
    Task<IReadOnlyList<MenuCategoryDto>> ListCategoriesAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuCategoryDto> GetCategoryAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuCategoryDto> CreateCategoryAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        CreateMenuCategoryCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuCategoryDto> UpdateCategoryAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        UpdateMenuCategoryCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuCategoryDto> ActivateCategoryAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuCategoryDto> DeactivateCategoryAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<IReadOnlyList<MenuCategoryDto>> ReorderCategoriesAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        ReorderCategoriesCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);
}
