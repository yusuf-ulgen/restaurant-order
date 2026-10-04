using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Catalog;

/// <summary>
/// Application service contract for menu and catalog operations.
/// Enforces tenant boundary isolation, branch-level access control, and optimistic concurrency.
/// </summary>
public interface ICatalogService : ICatalogAvailabilityService
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

    // Items
    Task<IReadOnlyList<MenuItemDto>> ListItemsAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId? categoryId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuItemDto> GetItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuItemDto> CreateItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        CreateMenuItemCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuItemDto> UpdateItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        UpdateMenuItemCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuItemDto> UpdateItemPriceAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        UpdateMenuItemPriceCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuItemDto> ActivateItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuItemDto> DeactivateItemAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<IReadOnlyList<MenuItemDto>> ReorderItemsAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        ReorderMenuItemsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    // Variants
    Task<IReadOnlyList<ItemVariantDto>> ListVariantsAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ItemVariantDto> GetVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ItemVariantDto> CreateVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        CreateItemVariantCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ItemVariantDto> UpdateVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        UpdateItemVariantCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ItemVariantDto> UpdateVariantPriceAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        UpdateItemVariantPriceCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ItemVariantDto> ActivateVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ItemVariantDto> DeactivateVariantAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ItemVariantId variantId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<IReadOnlyList<ItemVariantDto>> ReorderVariantsAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ReorderItemVariantsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    // Item Metadata & Modifier Group Assignments
    Task<MenuItemDto> UpdateItemMetadataAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        UpdateMenuItemMetadataCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuItemDto> AssignModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        AssignModifierGroupCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuItemDto> RemoveModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ModifierGroupId modifierGroupId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<MenuItemDto> ReorderMenuItemModifierGroupsAsync(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId itemId,
        ReorderMenuItemModifierGroupsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    // Modifier Groups
    Task<IReadOnlyList<ModifierGroupDto>> ListModifierGroupsAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierGroupDto> GetModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierGroupDto> CreateModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        CreateModifierGroupCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierGroupDto> UpdateModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        UpdateModifierGroupCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierGroupDto> ActivateModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierGroupDto> DeactivateModifierGroupAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    // Modifier Options
    Task<IReadOnlyList<ModifierOptionDto>> ListModifierOptionsAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierOptionDto> GetModifierOptionAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ModifierOptionId optionId,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierOptionDto> CreateModifierOptionAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        CreateModifierOptionCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierOptionDto> UpdateModifierOptionAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ModifierOptionId optionId,
        UpdateModifierOptionCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierOptionDto> UpdateModifierOptionPriceAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ModifierOptionId optionId,
        UpdateModifierOptionPriceCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierOptionDto> ActivateModifierOptionAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ModifierOptionId optionId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<ModifierOptionDto> DeactivateModifierOptionAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ModifierOptionId optionId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct);

    Task<IReadOnlyList<ModifierOptionDto>> ReorderModifierOptionsAsync(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        ReorderModifierOptionsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct);
}
