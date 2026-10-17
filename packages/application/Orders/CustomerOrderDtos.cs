namespace RestaurantOrder.Application.Orders;

/// <summary>
/// Selected modifier option in a customer order submission.
/// </summary>
public sealed record CustomerSelectedModifierRequest(
    Guid ModifierGroupId,
    Guid ModifierOptionId);

/// <summary>
/// Line item in a customer order submission.
/// </summary>
public sealed record CustomerOrderItemRequest(
    Guid MenuItemId,
    Guid? ItemVariantId,
    int Quantity,
    string? CustomerNote,
    IReadOnlyList<CustomerSelectedModifierRequest>? SelectedModifiers,
    long? ClientExpectedUnitPriceMinorUnits);

/// <summary>
/// Customer order creation payload.
/// </summary>
public sealed record CreateCustomerOrderRequest(
    IReadOnlyList<CustomerOrderItemRequest> Items);

/// <summary>
/// DTO representing an order modifier snapshot.
/// </summary>
public sealed record CustomerOrderItemModifierDto(
    Guid Id,
    Guid ModifierGroupId,
    string ModifierGroupName,
    Guid ModifierOptionId,
    string ModifierOptionName,
    long PriceDeltaMinorUnits);

/// <summary>
/// DTO representing an order line item snapshot.
/// </summary>
public sealed record CustomerOrderItemDto(
    Guid Id,
    Guid MenuItemId,
    Guid? ItemVariantId,
    string ItemName,
    string? VariantName,
    int Quantity,
    string? CustomerNote,
    long UnitPriceMinorUnits,
    long ModifierTotalMinorUnits,
    long LineTotalMinorUnits,
    int TaxRateBasisPoints,
    string Status,
    IReadOnlyList<CustomerOrderItemModifierDto> Modifiers);

/// <summary>
/// Complete customer-facing order response DTO with financial totals, round number, and status.
/// </summary>
public sealed record CustomerOrderResponseDto(
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
    IReadOnlyList<CustomerOrderItemDto> Items);

/// <summary>
/// Result of order creation indicating whether it was an existing replay.
/// </summary>
public sealed record CreateCustomerOrderResult(
    CustomerOrderResponseDto Order,
    bool IsReplay);
