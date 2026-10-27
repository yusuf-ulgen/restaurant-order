using System.Text.Json.Serialization;

namespace RestaurantOrder.Application.Events;

/// <summary>
/// Data payload for 'order.created' event.
/// Data-minimized: No customer notes or payment credentials included.
/// </summary>
public record OrderCreatedEventData(
    [property: JsonPropertyName("order_id")] Guid OrderId,
    [property: JsonPropertyName("dining_session_id")] Guid DiningSessionId,
    [property: JsonPropertyName("table_id")] Guid TableId,
    [property: JsonPropertyName("round_number")] int RoundNumber,
    [property: JsonPropertyName("order_source")] string OrderSource,
    [property: JsonPropertyName("item_count")] int ItemCount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc
);

/// <summary>
/// Data payload for 'order.item_status_changed' event.
/// </summary>
public record OrderItemStatusChangedEventData(
    [property: JsonPropertyName("order_id")] Guid OrderId,
    [property: JsonPropertyName("dining_session_id")] Guid DiningSessionId,
    [property: JsonPropertyName("item_id")] Guid ItemId,
    [property: JsonPropertyName("new_status")] string NewStatus,
    [property: JsonPropertyName("changed_at_utc")] DateTimeOffset ChangedAtUtc
);

/// <summary>
/// Data payload for 'table.service_requested' event.
/// </summary>
public record TableServiceRequestedEventData(
    [property: JsonPropertyName("service_request_id")] Guid ServiceRequestId,
    [property: JsonPropertyName("table_id")] Guid TableId,
    [property: JsonPropertyName("dining_session_id")] Guid DiningSessionId,
    [property: JsonPropertyName("request_type")] string RequestType,
    [property: JsonPropertyName("requested_at_utc")] DateTimeOffset RequestedAtUtc
);

/// <summary>
/// Data payload for 'table.service_request_status_changed' event.
/// </summary>
public record TableServiceRequestStatusChangedEventData(
    [property: JsonPropertyName("service_request_id")] Guid ServiceRequestId,
    [property: JsonPropertyName("table_id")] Guid TableId,
    [property: JsonPropertyName("dining_session_id")] Guid DiningSessionId,
    [property: JsonPropertyName("new_status")] string NewStatus,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc
);

/// <summary>
/// Data payload for 'menu.item_availability_changed' event.
/// </summary>
public record MenuItemAvailabilityChangedEventData(
    [property: JsonPropertyName("menu_item_id")] Guid MenuItemId,
    [property: JsonPropertyName("item_variant_id")] Guid? ItemVariantId,
    [property: JsonPropertyName("is_available")] bool IsAvailable,
    [property: JsonPropertyName("reason_code")] string? ReasonCode,
    [property: JsonPropertyName("changed_at_utc")] DateTimeOffset ChangedAtUtc
);

/// <summary>
/// Data payload for 'table.session_closed' event.
/// </summary>
public record TableSessionClosedEventData(
    [property: JsonPropertyName("dining_session_id")] Guid DiningSessionId,
    [property: JsonPropertyName("table_id")] Guid TableId,
    [property: JsonPropertyName("closed_at_utc")] DateTimeOffset ClosedAtUtc
);

/// <summary>
/// Data payload for 'catalog.policy_invalidated' event.
/// </summary>
public record CatalogPolicyInvalidatedEventData(
    [property: JsonPropertyName("policy_fingerprint")] string? PolicyFingerprint,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("invalidated_at_utc")] DateTimeOffset InvalidatedAtUtc
);

/// <summary>
/// Forward contract for Phase 11: 'kds.ticket_bumped'.
/// Reserved for Phase 11; do not emit in Phase 9.
/// </summary>
public record KdsTicketBumpedEventData(
    [property: JsonPropertyName("ticket_id")] Guid TicketId,
    [property: JsonPropertyName("table_id")] Guid TableId,
    [property: JsonPropertyName("station")] string Station,
    [property: JsonPropertyName("bumped_at_utc")] DateTimeOffset BumpedAtUtc
);
