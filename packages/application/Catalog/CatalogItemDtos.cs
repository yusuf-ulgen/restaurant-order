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
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken,
    IReadOnlyList<ItemVariantDto>? Variants = null);

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

public sealed record CreateMenuItemCommand(
    Guid CategoryId,
    string Name,
    string Slug,
    long BasePriceMinorUnits,
    string? ShortDescription = null,
    string? FullDescription = null,
    string? ImageUrl = null,
    int SortOrder = 0);

public sealed record UpdateMenuItemCommand(
    Guid CategoryId,
    string Name,
    string? ShortDescription,
    string? FullDescription,
    string? ImageUrl,
    int SortOrder,
    long? BasePriceMinorUnits = null,
    Guid? ConcurrencyToken = null);

public sealed record UpdateMenuItemPriceCommand(
    long BasePriceMinorUnits,
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
