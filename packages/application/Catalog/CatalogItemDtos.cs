namespace RestaurantOrder.Application.Catalog;

public sealed record MenuItemDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid MenuId,
    Guid CategoryId,
    string Name,
    string Slug,
    string? ShortDescription,
    string? FullDescription,
    string? ImageUrl,
    long BasePriceMinorUnits,
    int SortOrder,
    bool IsActive,
    int SpicyLevel,
    IReadOnlyList<string> DietaryTags,
    IReadOnlyList<string> AllergenTags,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken,
    Guid? PreparationStationId = null,
    IReadOnlyList<ItemVariantDto>? Variants = null,
    IReadOnlyList<MenuItemModifierGroupDto>? ModifierGroups = null);

public sealed record ItemVariantDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid MenuId,
    Guid MenuItemId,
    string Name,
    string Code,
    long AbsolutePriceMinorUnits,
    int SortOrder,
    bool IsDefault,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken);

public sealed record ModifierGroupDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string Name,
    int MinSelections,
    int MaxSelections,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken,
    IReadOnlyList<ModifierOptionDto>? Options = null);

public sealed record ModifierOptionDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid ModifierGroupId,
    string Name,
    long PriceDeltaMinorUnits,
    int SortOrder,
    bool IsDefault,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken);

public sealed record MenuItemModifierGroupDto(
    Guid ModifierGroupId,
    string Name,
    int MinSelections,
    int MaxSelections,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<ModifierOptionDto>? Options = null);

public sealed record CreateMenuItemCommand(
    Guid CategoryId,
    string Name,
    string Slug,
    long BasePriceMinorUnits,
    string? ShortDescription = null,
    string? FullDescription = null,
    string? ImageUrl = null,
    int SortOrder = 0,
    int SpicyLevel = 0,
    IReadOnlyList<string>? DietaryTags = null,
    IReadOnlyList<string>? AllergenTags = null,
    Guid? PreparationStationId = null);

public sealed record UpdateMenuItemCommand(
    Guid CategoryId,
    string Name,
    string? ShortDescription,
    string? FullDescription,
    string? ImageUrl,
    int SortOrder,
    long? BasePriceMinorUnits = null,
    Guid? ConcurrencyToken = null,
    Guid? PreparationStationId = null);

public sealed record UpdateMenuItemPriceCommand(
    long BasePriceMinorUnits,
    Guid? ConcurrencyToken = null);

public sealed record UpdateMenuItemMetadataCommand(
    int SpicyLevel,
    IReadOnlyList<string> DietaryTags,
    IReadOnlyList<string> AllergenTags,
    Guid? ConcurrencyToken = null);

public sealed record ItemReorderItem(
    Guid Id,
    int SortOrder,
    Guid ConcurrencyToken);

public sealed record ReorderMenuItemsCommand(
    IReadOnlyList<ItemReorderItem> Items);

public sealed record CreateItemVariantCommand(
    string Name,
    string Code,
    long AbsolutePriceMinorUnits,
    int SortOrder = 0,
    bool IsDefault = false);

public sealed record UpdateItemVariantCommand(
    string Name,
    string Code,
    int SortOrder,
    bool IsDefault,
    long? AbsolutePriceMinorUnits = null,
    Guid? ConcurrencyToken = null);

public sealed record UpdateItemVariantPriceCommand(
    long AbsolutePriceMinorUnits,
    Guid? ConcurrencyToken = null);

public sealed record VariantReorderItem(
    Guid Id,
    int SortOrder,
    Guid ConcurrencyToken);

public sealed record ReorderItemVariantsCommand(
    IReadOnlyList<VariantReorderItem> Items);

public sealed record CreateModifierGroupCommand(
    string Name,
    int MinSelections,
    int MaxSelections,
    int SortOrder = 0);

public sealed record UpdateModifierGroupCommand(
    string Name,
    int MinSelections,
    int MaxSelections,
    int SortOrder,
    Guid? ConcurrencyToken = null);

public sealed record CreateModifierOptionCommand(
    string Name,
    long PriceDeltaMinorUnits,
    int SortOrder = 0,
    bool IsDefault = false);

public sealed record UpdateModifierOptionCommand(
    string Name,
    int SortOrder,
    bool IsDefault,
    long? PriceDeltaMinorUnits = null,
    Guid? ConcurrencyToken = null);

public sealed record UpdateModifierOptionPriceCommand(
    long PriceDeltaMinorUnits,
    Guid? ConcurrencyToken = null);

public sealed record ModifierOptionReorderItem(
    Guid Id,
    int SortOrder,
    Guid ConcurrencyToken);

public sealed record ReorderModifierOptionsCommand(
    IReadOnlyList<ModifierOptionReorderItem> Items);

public sealed record AssignModifierGroupCommand(
    Guid ModifierGroupId,
    int SortOrder = 0,
    Guid? ConcurrencyToken = null);

public sealed record ModifierGroupAssignmentReorderItem(
    Guid ModifierGroupId,
    int SortOrder);

public sealed record ReorderMenuItemModifierGroupsCommand(
    IReadOnlyList<ModifierGroupAssignmentReorderItem> Items,
    Guid? ConcurrencyToken = null);
