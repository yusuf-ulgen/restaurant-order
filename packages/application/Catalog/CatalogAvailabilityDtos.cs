namespace RestaurantOrder.Application.Catalog;

public sealed record BranchItemAvailabilityDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid MenuItemId,
    Guid? ItemVariantId,
    bool IsAvailable,
    string ReasonCode,
    string? Note,
    DateTime? ExpectedAvailableAtUtc,
    Guid ChangedByUserId,
    DateTime ChangedAtUtc,
    Guid ConcurrencyToken);

public sealed record Quick86ItemCommand(
    string ReasonCode,
    string? Note = null,
    DateTime? ExpectedAvailableAtUtc = null,
    Guid? ConcurrencyToken = null);

public sealed record Quick86VariantCommand(
    string ReasonCode,
    string? Note = null,
    DateTime? ExpectedAvailableAtUtc = null,
    Guid? ConcurrencyToken = null);

public sealed record RestockItemCommand(
    string? Note = null,
    Guid? ConcurrencyToken = null);

public sealed record RestockVariantCommand(
    string? Note = null,
    Guid? ConcurrencyToken = null);

/// <summary>
/// Effective customer/runtime menu read model for a branch.
/// Contains ONLY active menus, categories, items, variants, modifier groups, and options.
/// Calculates effective availability (item and variant level) and strips all internal/admin metadata.
/// </summary>
public sealed record RuntimeMenuReadModel(
    Guid BranchId,
    string Currency,
    IReadOnlyList<RuntimeMenuDto> Menus);

public sealed record RuntimeMenuDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    int SortOrder,
    IReadOnlyList<RuntimeMenuCategoryDto> Categories);

public sealed record RuntimeMenuCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    int SortOrder,
    IReadOnlyList<RuntimeMenuItemDto> Items);

public sealed record RuntimeMenuItemDto(
    Guid Id,
    string Name,
    string Slug,
    string? ShortDescription,
    string? FullDescription,
    string? ImageUrl,
    long BasePriceMinorUnits,
    int SortOrder,
    int SpicyLevel,
    IReadOnlyList<string> DietaryTags,
    IReadOnlyList<string> AllergenTags,
    bool IsAvailable,
    DateTime? ExpectedAvailableAtUtc,
    IReadOnlyList<RuntimeItemVariantDto> Variants,
    IReadOnlyList<RuntimeModifierGroupDto> ModifierGroups);

public sealed record RuntimeItemVariantDto(
    Guid Id,
    string Name,
    string Code,
    long AbsolutePriceMinorUnits,
    int SortOrder,
    bool IsDefault,
    bool IsAvailable,
    DateTime? ExpectedAvailableAtUtc);

public sealed record RuntimeModifierGroupDto(
    Guid Id,
    string Name,
    int MinSelections,
    int MaxSelections,
    int SortOrder,
    IReadOnlyList<RuntimeModifierOptionDto> Options);

public sealed record RuntimeModifierOptionDto(
    Guid Id,
    string Name,
    long PriceDeltaMinorUnits,
    int SortOrder,
    bool IsDefault);
