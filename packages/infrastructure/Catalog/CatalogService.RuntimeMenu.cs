using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Catalog;

public partial class CatalogService
{
    public async Task<RuntimeMenuReadModel> GetRuntimeMenuAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);

        // Fetch active menus
        var menus = await _dbContext.Menus
            .Where(m => m.TenantId == tenantId && m.BranchId == branchId && m.Status == MenuStatus.Active)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Name)
            .ToListAsync(ct);

        if (menus.Count == 0)
        {
            return new RuntimeMenuReadModel(
                BranchId: branchId.Value,
                Currency: branch.Currency.Code,
                Menus: Array.Empty<RuntimeMenuDto>());
        }

        var menuIds = menus.Select(m => m.Id).ToHashSet();

        // Fetch active categories
        var categories = await _dbContext.MenuCategories
            .Where(c => c.TenantId == tenantId && c.BranchId == branchId && menuIds.Contains(c.MenuId) && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        // Fetch active items with active variants
        var items = await _dbContext.MenuItems
            .Where(i => i.TenantId == tenantId && i.BranchId == branchId && menuIds.Contains(i.MenuId) && i.IsActive)
            .Include(i => i.Variants)
            .Include(i => i.ModifierGroupAssignments)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Name)
            .ToListAsync(ct);

        // Fetch active modifier groups and active options
        var modifierGroupIds = items
            .SelectMany(i => i.ModifierGroupAssignments)
            .Select(a => a.ModifierGroupId)
            .Distinct()
            .ToList();

        var modifierGroups = await _dbContext.ModifierGroups
            .Where(g => g.TenantId == tenantId && g.BranchId == branchId && modifierGroupIds.Contains(g.Id) && g.IsActive)
            .Include(g => g.Options)
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Name)
            .ToListAsync(ct);

        var modifierGroupMap = modifierGroups.ToDictionary(g => g.Id);

        // Fetch availability overrides for branch
        var availabilities = await _dbContext.BranchItemAvailabilities
            .Where(a => a.TenantId == tenantId && a.BranchId == branchId)
            .ToListAsync(ct);

        var itemAvailabilityMap = availabilities
            .Where(a => a.ItemVariantId == null)
            .ToDictionary(a => a.MenuItemId);

        var variantAvailabilityMap = availabilities
            .Where(a => a.ItemVariantId != null)
            .ToDictionary(a => (a.MenuItemId, a.ItemVariantId!.Value));

        // Group categories by menu
        var categoriesByMenu = categories.GroupBy(c => c.MenuId).ToDictionary(g => g.Key, g => g.ToList());

        // Group items by category
        var itemsByCategory = items.GroupBy(i => i.CategoryId).ToDictionary(g => g.Key, g => g.ToList());

        var runtimeMenus = new List<RuntimeMenuDto>();

        foreach (var menu in menus)
        {
            var menuCategories = new List<RuntimeMenuCategoryDto>();

            if (categoriesByMenu.TryGetValue(menu.Id, out var catList))
            {
                foreach (var category in catList.OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
                {
                    var runtimeItems = new List<RuntimeMenuItemDto>();

                    if (itemsByCategory.TryGetValue(category.Id, out var itemList))
                    {
                        foreach (var item in itemList.OrderBy(i => i.SortOrder).ThenBy(i => i.Name))
                        {
                            var isItemAvailable = true;
                            DateTime? itemExpectedAt = null;

                            if (itemAvailabilityMap.TryGetValue(item.Id, out var itemAvail))
                            {
                                isItemAvailable = itemAvail.IsAvailable;
                                itemExpectedAt = itemAvail.ExpectedAvailableAtUtc;
                            }

                            // Compute active variants
                            var runtimeVariants = new List<RuntimeItemVariantDto>();
                            foreach (var variant in item.Variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder).ThenBy(v => v.Name))
                            {
                                bool isVariantAvailable;
                                DateTime? variantExpectedAt;

                                if (!isItemAvailable)
                                {
                                    // When item is 86'd, ALL its variants must appear unavailable in runtime read model
                                    isVariantAvailable = false;
                                    variantExpectedAt = itemExpectedAt;
                                }
                                else
                                {
                                    if (variantAvailabilityMap.TryGetValue((item.Id, variant.Id), out var varAvail))
                                    {
                                        isVariantAvailable = varAvail.IsAvailable;
                                        variantExpectedAt = varAvail.ExpectedAvailableAtUtc;
                                    }
                                    else
                                    {
                                        isVariantAvailable = true;
                                        variantExpectedAt = null;
                                    }
                                }

                                runtimeVariants.Add(new RuntimeItemVariantDto(
                                    Id: variant.Id.Value,
                                    Name: variant.Name,
                                    Code: variant.Code,
                                    AbsolutePriceMinorUnits: variant.AbsolutePriceMinorUnits.MinorUnits,
                                    SortOrder: variant.SortOrder,
                                    IsDefault: variant.IsDefault,
                                    IsAvailable: isVariantAvailable,
                                    ExpectedAvailableAtUtc: variantExpectedAt));
                            }

                            // Compute active modifier groups
                            var runtimeGroups = new List<RuntimeModifierGroupDto>();
                            foreach (var assignment in item.ModifierGroupAssignments.OrderBy(a => a.SortOrder))
                            {
                                if (modifierGroupMap.TryGetValue(assignment.ModifierGroupId, out var group))
                                {
                                    var activeOptions = group.Options
                                        .Where(o => o.IsActive)
                                        .OrderBy(o => o.SortOrder)
                                        .ThenBy(o => o.Name)
                                        .Select(o => new RuntimeModifierOptionDto(
                                            Id: o.Id.Value,
                                            Name: o.Name,
                                            PriceDeltaMinorUnits: o.PriceDeltaMinorUnits.MinorUnits,
                                            SortOrder: o.SortOrder,
                                            IsDefault: o.IsDefault))
                                        .ToList();

                                    runtimeGroups.Add(new RuntimeModifierGroupDto(
                                        Id: group.Id.Value,
                                        Name: group.Name,
                                        MinSelections: group.MinSelections,
                                        MaxSelections: group.MaxSelections,
                                        SortOrder: assignment.SortOrder,
                                        Options: activeOptions));
                                }
                            }

                            runtimeItems.Add(new RuntimeMenuItemDto(
                                Id: item.Id.Value,
                                Name: item.Name,
                                Slug: item.Slug,
                                ShortDescription: item.ShortDescription,
                                FullDescription: item.FullDescription,
                                ImageUrl: item.ImageUrl,
                                BasePriceMinorUnits: item.BasePriceMinorUnits.MinorUnits,
                                SortOrder: item.SortOrder,
                                SpicyLevel: item.SpicyLevel.Value,
                                DietaryTags: item.DietaryTags.Select(t => t.ToString()).OrderBy(t => t).ToList(),
                                AllergenTags: item.AllergenTags.Select(a => a.ToString()).OrderBy(a => a).ToList(),
                                IsAvailable: isItemAvailable,
                                ExpectedAvailableAtUtc: itemExpectedAt,
                                Variants: runtimeVariants,
                                ModifierGroups: runtimeGroups));
                        }
                    }

                    menuCategories.Add(new RuntimeMenuCategoryDto(
                        Id: category.Id.Value,
                        Name: category.Name,
                        Slug: category.Slug,
                        Description: category.Description,
                        SortOrder: category.SortOrder,
                        Items: runtimeItems));
                }
            }

            runtimeMenus.Add(new RuntimeMenuDto(
                Id: menu.Id.Value,
                Name: menu.Name,
                Slug: menu.Slug,
                Description: menu.Description,
                SortOrder: menu.SortOrder,
                Categories: menuCategories));
        }

        return new RuntimeMenuReadModel(
            BranchId: branchId.Value,
            Currency: branch.Currency.Code,
            Menus: runtimeMenus);
    }
}
