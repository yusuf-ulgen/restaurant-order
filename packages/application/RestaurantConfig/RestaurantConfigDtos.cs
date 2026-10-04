namespace RestaurantOrder.Application.RestaurantConfig;

public sealed record BrandDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Slug,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken);

public sealed record CreateBrandCommand(
    string Name,
    string Slug);

public sealed record UpdateBrandCommand(
    string Name,
    Guid? ConcurrencyToken = null);

public sealed record BrandStateChangeCommand(
    Guid? ConcurrencyToken = null);

public sealed record BranchDto(
    Guid Id,
    Guid TenantId,
    Guid BrandId,
    string Name,
    string Slug,
    string Timezone,
    string Currency,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid ConcurrencyToken);

public sealed record CreateBranchCommand(
    Guid BrandId,
    string Name,
    string Slug,
    string? Timezone = null,
    string? Currency = null);

public sealed record UpdateBranchCommand(
    string Name,
    string Timezone,
    string Currency,
    Guid? ConcurrencyToken = null);

public sealed record BranchStateChangeCommand(
    string? Reason = null,
    Guid? ConcurrencyToken = null);
