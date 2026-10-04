using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Infrastructure.Catalog;

public partial class CatalogService
{
    private static MenuDto MapMenu(Menu menu) => new(
        Id: menu.Id.Value,
        TenantId: menu.TenantId.Value,
        BranchId: menu.BranchId.Value,
        Name: menu.Name,
        Slug: menu.Slug,
        Description: menu.Description,
        Status: menu.Status.ToString(),
        SortOrder: menu.SortOrder,
        CreatedAtUtc: menu.CreatedAtUtc,
        UpdatedAtUtc: menu.UpdatedAtUtc,
        ConcurrencyToken: menu.ConcurrencyToken);

    private static ItemVariantDto MapItemVariant(ItemVariant variant) => new(
        Id: variant.Id.Value,
        TenantId: variant.TenantId.Value,
        BranchId: variant.BranchId.Value,
        MenuId: variant.MenuId.Value,
        MenuItemId: variant.MenuItemId.Value,
        Name: variant.Name,
        Code: variant.Code,
        AbsolutePriceMinorUnits: variant.AbsolutePriceMinorUnits.MinorUnits,
        SortOrder: variant.SortOrder,
        IsDefault: variant.IsDefault,
        IsActive: variant.IsActive,
        CreatedAtUtc: variant.CreatedAtUtc,
        UpdatedAtUtc: variant.UpdatedAtUtc,
        ConcurrencyToken: variant.ConcurrencyToken);

    private static MenuItemDto MapMenuItem(MenuItem item) => new(
        Id: item.Id.Value,
        TenantId: item.TenantId.Value,
        BranchId: item.BranchId.Value,
        MenuId: item.MenuId.Value,
        CategoryId: item.CategoryId.Value,
        Name: item.Name,
        Slug: item.Slug,
        ShortDescription: item.ShortDescription,
        FullDescription: item.FullDescription,
        ImageUrl: item.ImageUrl,
        BasePriceMinorUnits: item.BasePriceMinorUnits.MinorUnits,
        SortOrder: item.SortOrder,
        IsActive: item.IsActive,
        SpicyLevel: item.SpicyLevel.Value,
        DietaryTags: item.DietaryTags.Select(t => t.ToString()).ToList(),
        AllergenTags: item.AllergenTags.Select(a => a.ToString()).ToList(),
        CreatedAtUtc: item.CreatedAtUtc,
        UpdatedAtUtc: item.UpdatedAtUtc,
        ConcurrencyToken: item.ConcurrencyToken,
        Variants: item.Variants?.Select(MapItemVariant).ToList(),
        ModifierGroups: item.ModifierGroupAssignments?.OrderBy(a => a.SortOrder).Select(a => new MenuItemModifierGroupDto(
            a.ModifierGroupId.Value,
            a.ModifierGroup?.Name ?? string.Empty,
            a.ModifierGroup?.MinSelections ?? 0,
            a.ModifierGroup?.MaxSelections ?? 0,
            a.SortOrder,
            a.ModifierGroup?.IsActive ?? true,
            a.ModifierGroup?.Options?.OrderBy(o => o.SortOrder).Select(MapModifierOption).ToList()
        )).ToList());

    private static ModifierGroupDto MapModifierGroup(ModifierGroup group) => new(
        Id: group.Id.Value,
        TenantId: group.TenantId.Value,
        BranchId: group.BranchId.Value,
        Name: group.Name,
        MinSelections: group.MinSelections,
        MaxSelections: group.MaxSelections,
        SortOrder: group.SortOrder,
        IsActive: group.IsActive,
        CreatedAtUtc: group.CreatedAtUtc,
        UpdatedAtUtc: group.UpdatedAtUtc,
        ConcurrencyToken: group.ConcurrencyToken,
        Options: group.Options?.OrderBy(o => o.SortOrder).Select(MapModifierOption).ToList());

    private static ModifierOptionDto MapModifierOption(ModifierOption option) => new(
        Id: option.Id.Value,
        TenantId: option.TenantId.Value,
        BranchId: option.BranchId.Value,
        ModifierGroupId: option.ModifierGroupId.Value,
        Name: option.Name,
        PriceDeltaMinorUnits: option.PriceDeltaMinorUnits.MinorUnits,
        SortOrder: option.SortOrder,
        IsDefault: option.IsDefault,
        IsActive: option.IsActive,
        CreatedAtUtc: option.CreatedAtUtc,
        UpdatedAtUtc: option.UpdatedAtUtc,
        ConcurrencyToken: option.ConcurrencyToken);

    public static HashSet<DietaryTag> ParseDietaryTags(IEnumerable<string>? tags)
    {
        var result = new HashSet<DietaryTag>();
        if (tags == null) return result;

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;

            if (Enum.TryParse<DietaryTag>(tag.Trim(), ignoreCase: true, out var parsed))
            {
                result.Add(parsed);
            }
            else
            {
                throw new DomainException($"Invalid dietary tag '{tag}'. Valid tags are: {string.Join(", ", Enum.GetNames<DietaryTag>())}");
            }
        }

        return result;
    }

    public static HashSet<AllergenTag> ParseAllergenTags(IEnumerable<string>? tags)
    {
        var result = new HashSet<AllergenTag>();
        if (tags == null) return result;

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;

            if (Enum.TryParse<AllergenTag>(tag.Trim(), ignoreCase: true, out var parsed))
            {
                result.Add(parsed);
            }
            else
            {
                throw new DomainException($"Invalid allergen tag '{tag}'. Valid tags are: {string.Join(", ", Enum.GetNames<AllergenTag>())}");
            }
        }

        return result;
    }
}
