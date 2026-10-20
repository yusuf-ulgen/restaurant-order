using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Orders;

/// <summary>
/// Immutable audit entry recording state changes and operational interventions on an Order.
/// Append-only record; updates and deletes are prohibited both in domain logic and database constraints.
/// </summary>
public sealed class OrderAuditEntry
{
    public const int MaxActionLength = 50;
    public const int MaxActorRoleLength = 50;
    public const int MaxReasonLength = 500;
    public const int MaxDetailsLength = 2000;

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public OrderId OrderId { get; private set; }
    public DiningSessionId DiningSessionId { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorRole { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public OrderStatus? PreviousStatus { get; private set; }
    public OrderStatus? NewStatus { get; private set; }
    public string? Reason { get; private set; }
    public string? DetailsJson { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // Parameterless constructor for EF Core materialization
    private OrderAuditEntry()
    {
    }

    public static OrderAuditEntry Record(
        TenantId tenantId,
        OrderId orderId,
        DiningSessionId diningSessionId,
        Guid actorId,
        string actorRole,
        string action,
        OrderStatus? previousStatus = null,
        OrderStatus? newStatus = null,
        string? reason = null,
        string? detailsJson = null,
        DateTimeOffset? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new DomainException("Action cannot be empty for an audit entry.");
        }

        var trimmedAction = action.Trim();
        if (trimmedAction.Length > MaxActionLength)
        {
            throw new DomainException($"Action cannot exceed {MaxActionLength} characters.");
        }

        var trimmedRole = string.IsNullOrWhiteSpace(actorRole) ? "System" : actorRole.Trim();
        if (trimmedRole.Length > MaxActorRoleLength)
        {
            throw new DomainException($"Actor role cannot exceed {MaxActorRoleLength} characters.");
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason != null && trimmedReason.Length > MaxReasonLength)
        {
            throw new DomainException($"Reason cannot exceed {MaxReasonLength} characters.");
        }

        var trimmedDetails = string.IsNullOrWhiteSpace(detailsJson) ? null : detailsJson.Trim();
        if (trimmedDetails != null && trimmedDetails.Length > MaxDetailsLength)
        {
            throw new DomainException($"Details cannot exceed {MaxDetailsLength} characters.");
        }

        return new OrderAuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            DiningSessionId = diningSessionId,
            ActorId = actorId,
            ActorRole = trimmedRole,
            Action = trimmedAction,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Reason = trimmedReason,
            DetailsJson = trimmedDetails,
            CreatedAtUtc = nowUtc ?? DateTimeOffset.UtcNow
        };
    }
}
