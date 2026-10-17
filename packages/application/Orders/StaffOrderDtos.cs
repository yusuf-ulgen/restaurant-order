namespace RestaurantOrder.Application.Orders;

/// <summary>
/// Selected modifier option in a staff order submission.
/// </summary>
public sealed record StaffSelectedModifierRequest(
    Guid ModifierGroupId,
    Guid ModifierOptionId);

/// <summary>
/// Line item in a staff order submission.
/// </summary>
public sealed record StaffOrderItemRequest(
    Guid MenuItemId,
    Guid? ItemVariantId,
    int Quantity,
    string? CustomerNote,
    IReadOnlyList<StaffSelectedModifierRequest>? SelectedModifiers,
    long? ClientExpectedUnitPriceMinorUnits);

/// <summary>
/// Staff order creation request body.
/// </summary>
public sealed record CreateStaffOrderRequest(
    IReadOnlyList<StaffOrderItemRequest> Items);

/// <summary>
/// Request body for cancelling or voiding an order item.
/// </summary>
public sealed record CancelOrderItemRequest(
    string Reason,
    string? SupervisorPin = null,
    Guid? SupervisorUserId = null,
    Guid? ConcurrencyToken = null);

/// <summary>
/// DTO representing modifier snapshot in staff order views.
/// </summary>
public sealed record StaffOrderItemModifierDto(
    Guid Id,
    Guid ModifierGroupId,
    string ModifierGroupName,
    Guid ModifierOptionId,
    string ModifierOptionName,
    long PriceDeltaMinorUnits);

/// <summary>
/// DTO representing an order line item in staff views.
/// </summary>
public sealed record StaffOrderItemDto(
    Guid Id,
    Guid MenuItemId,
    Guid? ItemVariantId,
    string ItemName,
    string? VariantName,
    int Quantity,
    string? CustomerNote,
    Guid? PreparationStationId,
    long UnitPriceMinorUnits,
    long ModifierTotalMinorUnits,
    long LineTotalMinorUnits,
    int TaxRateBasisPoints,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<StaffOrderItemModifierDto> Modifiers);

/// <summary>
/// Complete staff-facing order response DTO.
/// </summary>
public sealed record StaffOrderResponseDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid DiningSessionId,
    Guid TableId,
    int RoundNumber,
    string OrderSource,
    string Status,
    string Currency,
    long SubtotalMinorUnits,
    long TaxMinorUnits,
    long ServiceChargeMinorUnits,
    long TotalMinorUnits,
    string IdempotencyKey,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? CancellationReason,
    Guid? CancelledByActorId,
    IReadOnlyList<StaffOrderItemDto> Items);

/// <summary>
/// Result of staff order creation indicating whether it was an existing replay.
/// </summary>
public sealed record CreateStaffOrderResult(
    StaffOrderResponseDto Order,
    bool IsReplay);

/// <summary>
/// Result for lifecycle transitions (Phase 11 application service boundaries).
/// </summary>
public sealed record OrderLifecycleResult(
    StaffOrderResponseDto Order,
    string Action,
    DateTimeOffset TimestampUtc);
