namespace RestaurantOrder.Application.Catalog;

public sealed record MenuDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string Name,
    string Slug,
    string? Description,
    string Status,
    int SortOrder,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken);

public sealed record CreateMenuCommand(
    string Name,
    string Slug,
    string? Description = null,
    int SortOrder = 0);

public sealed record UpdateMenuCommand(
    string Name,
    string? Description,
    int SortOrder,
    Guid? ConcurrencyToken = null);

public sealed record MenuCategoryDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid MenuId,
    string Name,
    string Slug,
    string? Description,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken);

public sealed record CreateMenuCategoryCommand(
    string Name,
    string Slug,
    string? Description = null,
    int SortOrder = 0);

public sealed record UpdateMenuCategoryCommand(
    string Name,
    string? Description,
    int SortOrder,
    Guid? ConcurrencyToken = null);

public sealed record CategoryReorderItem(
    Guid Id,
    int SortOrder,
    Guid ConcurrencyToken);

public sealed record ReorderCategoriesCommand(
    IReadOnlyList<CategoryReorderItem> Items);
