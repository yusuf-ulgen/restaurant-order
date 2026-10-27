using RestaurantOrder.Domain.Floor;

namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Payload to create an in-person customer service request.
/// Note: Target tenant, branch, dining session, and table are strictly derived
/// from the verified customer session claims and session entity.
/// </summary>
public record CreateCustomerServiceRequest(
    ServiceRequestType RequestType,
    string? Note = null);

/// <summary>
/// Data transfer object for a customer service request.
/// </summary>
public record ServiceRequestDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid DiningSessionId,
    Guid TableId,
    string RequestType,
    string Status,
    string? Note,
    string IdempotencyKey,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    Guid ConcurrencyToken);

/// <summary>
/// Staff-facing view of a customer service request enriched with table number.
/// </summary>
public record StaffServiceRequestDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid DiningSessionId,
    Guid TableId,
    string TableNumber,
    string RequestType,
    string Status,
    string? Note,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    Guid ConcurrencyToken);
