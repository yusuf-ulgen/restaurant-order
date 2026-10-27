using System.Text.Json.Serialization;

namespace RestaurantOrder.Application.Events;

/// <summary>
/// Query parameters for HTTP reconciliation sync.
/// </summary>
public record EventsSyncRequest(
    [property: JsonPropertyName("since_sequence_number")] long? SinceSequenceNumber = null,
    [property: JsonPropertyName("limit")] int Limit = 50
);

/// <summary>
/// Response for HTTP reconciliation sync.
/// </summary>
public record EventsSyncResponse(
    [property: JsonPropertyName("events")] IReadOnlyList<EventEnvelope<object>> Events,
    [property: JsonPropertyName("latest_sequence_number")] long LatestSequenceNumber,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("synced_at_utc")] DateTimeOffset SyncedAtUtc
);

/// <summary>
/// Request to register a device / installation ID for FCM push notifications.
/// </summary>
public record RegisterDeviceRequest(
    [property: JsonPropertyName("installation_id")] string InstallationId,
    [property: JsonPropertyName("platform")] string Platform
);

/// <summary>
/// Response for a device registration.
/// </summary>
public record DeviceRegistrationResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("installation_id")] string InstallationId,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("registered_at_utc")] DateTimeOffset RegisteredAtUtc,
    [property: JsonPropertyName("expires_at_utc")] DateTimeOffset ExpiresAtUtc
);
