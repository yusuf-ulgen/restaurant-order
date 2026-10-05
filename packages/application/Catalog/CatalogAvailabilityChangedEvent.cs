namespace RestaurantOrder.Application.Catalog;

/// <summary>
/// Type-safe application/domain event contract emitted when an item or variant availability state changes.
/// Used downstream by real-time notification brokers (e.g. SignalR hub in Phase 9) or outbox dispatchers.
/// Strictly published AFTER database transaction commit.
/// </summary>
public sealed record CatalogAvailabilityChangedEvent(
    Guid TenantId,
    Guid BranchId,
    Guid MenuItemId,
    Guid? ItemVariantId,
    bool IsAvailable,
    string ReasonCode,
    string? Note,
    DateTime? ExpectedAvailableAtUtc,
    DateTime OccurredAtUtc);
