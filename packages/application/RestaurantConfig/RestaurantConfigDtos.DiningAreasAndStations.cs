namespace RestaurantOrder.Application.RestaurantConfig;

// Dining Area DTOs
public sealed record DiningAreaDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string Name,
    string Code,
    string AreaType,
    int SortOrder,
    bool IsActive,
    Guid ConcurrencyToken,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record CreateDiningAreaCommand(
    string Name,
    string Code,
    string AreaType,
    int SortOrder = 0);

public sealed record UpdateDiningAreaCommand(
    string Name,
    string AreaType,
    Guid? ConcurrencyToken = null);

public sealed record ReorderDiningAreasCommand(
    IReadOnlyList<Guid> OrderedAreaIds);

// Preparation Station DTOs
public sealed record PreparationStationDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string Code,
    string DisplayName,
    string StationType,
    int SortOrder,
    bool IsActive,
    Guid ConcurrencyToken,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record CreatePreparationStationCommand(
    string Code,
    string DisplayName,
    string StationType,
    int SortOrder = 0);

public sealed record UpdatePreparationStationCommand(
    string DisplayName,
    string StationType,
    Guid? ConcurrencyToken = null);

public sealed record ReorderPreparationStationsCommand(
    IReadOnlyList<Guid> OrderedStationIds);

public sealed record StationRuntimeDto(
    Guid Id,
    string Code,
    string DisplayName,
    string StationType,
    int SortOrder,
    bool IsActive);

// Feature Flag DTOs
public sealed record FeatureFlagItemDto(
    string Key,
    bool IsEnabled,
    string Source); // "Default" | "Tenant" | "BranchOverride"

public sealed record TenantFeatureFlagsDto(
    Guid TenantId,
    IReadOnlyList<FeatureFlagItemDto> Flags,
    Guid ConcurrencyToken,
    DateTime? UpdatedAtUtc);

public sealed record BranchFeatureFlagsDto(
    Guid TenantId,
    Guid BranchId,
    IReadOnlyList<FeatureFlagItemDto> Overrides,
    Guid ConcurrencyToken,
    DateTime? UpdatedAtUtc);

public sealed record EffectiveFeatureFlagsDto(
    Guid BranchId,
    IReadOnlyList<FeatureFlagItemDto> Flags,
    IReadOnlyDictionary<string, bool> EvaluatedFlags);

public sealed record UpdateFeatureFlagsCommand(
    IReadOnlyDictionary<string, bool> Flags,
    Guid? ConcurrencyToken = null);
