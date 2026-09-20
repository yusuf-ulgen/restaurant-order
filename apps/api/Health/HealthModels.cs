using System.Text.Json.Serialization;

namespace RestaurantOrder.Api.Health;

/// <summary>
/// Model for the /health/live endpoint response.
/// Indicates whether the API application process is running.
/// Never includes external dependency state or sensitive credentials.
/// </summary>
public sealed record HealthLiveResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("service")] string Service,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("color")] string Color
);

/// <summary>
/// Status of external dependencies verified during readiness probes.
/// </summary>
public sealed record HealthChecks(
    [property: JsonPropertyName("database")] string Database,
    [property: JsonPropertyName("redis")] string Redis
);

/// <summary>
/// Model for the /health/ready endpoint response.
/// Indicates whether all required backing services are reachable.
/// </summary>
public sealed record HealthReadyResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("service")] string Service,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("color")] string Color,
    [property: JsonPropertyName("checks")] HealthChecks Checks
);
