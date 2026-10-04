namespace RestaurantOrder.Application.RestaurantConfig;

public sealed record TimeSlotDto(
    string OpenTime,
    string CloseTime,
    bool IsOvernight);

public sealed record OperatingDayScheduleDto(
    int DayOfWeek,
    bool IsClosed,
    IReadOnlyList<TimeSlotDto> Slots);

public sealed record BranchOperatingHoursDto(
    Guid BranchId,
    IReadOnlyList<OperatingDayScheduleDto> Days,
    Guid ConcurrencyToken,
    DateTime? UpdatedAtUtc);

public sealed record UpdateBranchOperatingHoursCommand(
    IReadOnlyList<OperatingDayScheduleDto> Days,
    Guid? ConcurrencyToken = null);

public sealed record BranchSettingsDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string Timezone,
    string Currency,
    string DefaultLocale,
    IReadOnlyList<string> SupportedLocales,
    bool PricesIncludeTax,
    int DefaultTaxRateBps,
    bool IsServiceChargeEnabled,
    int ServiceChargeRateBps,
    bool IsOrderTakingEnabled,
    string? DisplayName,
    string? PhoneNumber,
    string? Email,
    string? Address,
    Guid ConcurrencyToken,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record EffectiveBranchSettingsDto(
    Guid BranchId,
    string BranchName,
    string Timezone,
    string Currency,
    string DefaultLocale,
    IReadOnlyList<string> SupportedLocales,
    bool PricesIncludeTax,
    int DefaultTaxRateBps,
    bool IsServiceChargeEnabled,
    int ServiceChargeRateBps,
    bool IsOrderTakingEnabled,
    string? DisplayName,
    string? PhoneNumber,
    string? Email,
    string? Address,
    bool HasCustomSettings,
    Guid ConcurrencyToken);

public sealed record UpdateBranchSettingsCommand(
    string Timezone,
    string Currency,
    string DefaultLocale,
    IReadOnlyList<string> SupportedLocales,
    bool PricesIncludeTax,
    int DefaultTaxRateBps,
    bool IsServiceChargeEnabled,
    int ServiceChargeRateBps,
    bool IsOrderTakingEnabled,
    string? DisplayName = null,
    string? PhoneNumber = null,
    string? Email = null,
    string? Address = null,
    Guid? ConcurrencyToken = null);
