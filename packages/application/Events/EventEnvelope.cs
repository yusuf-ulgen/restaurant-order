using System.Text.Json.Serialization;

namespace RestaurantOrder.Application.Events;

/// <summary>
/// Constants for well-known real-time domain event types.
/// Corresponds to RealtimeEventType in @restaurant-order/contracts.
/// </summary>
public static class RealtimeEventTypes
{
    public const string OrderCreated = "order.created";
    public const string OrderItemStatusChanged = "order.item_status_changed";
    public const string TableServiceRequested = "table.service_requested";
    public const string TableServiceRequestStatusChanged = "table.service_request_status_changed";
    public const string MenuItemAvailabilityChanged = "menu.item_availability_changed";
    public const string TableSessionClosed = "table.session_closed";
    public const string CatalogPolicyInvalidated = "catalog.policy_invalidated";

    /// <summary>
    /// Phase 11 forward contract: station ticket bumped on KDS.
    /// Strictly reserved for Phase 11; do not emit in Phase 9.
    /// </summary>
    public const string KdsTicketBumped = "kds.ticket_bumped";
}

/// <summary>
/// Standard versioned event envelope matching ADR-0004 and @restaurant-order/contracts events.ts.
/// All properties are serialized as snake_case on the wire.
/// </summary>
public record EventEnvelope<T>(
    [property: JsonPropertyName("event_id")] Guid EventId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("occurred_at_utc")] DateTimeOffset OccurredAtUtc,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("branch_id")] Guid BranchId,
    [property: JsonPropertyName("aggregate_id")] string AggregateId,
    [property: JsonPropertyName("sequence_number")] long SequenceNumber,
    [property: JsonPropertyName("data")] T Data
);
