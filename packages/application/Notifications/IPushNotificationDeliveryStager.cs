using System;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantOrder.Application.Notifications;

/// <summary>
/// Stages persistent push notification delivery tasks for eligible device registrations
/// based on domain event metadata, recipient role/station scoping, and session affinity.
/// </summary>
public interface IPushNotificationDeliveryStager
{
    Task<int> StageDeliveriesForEventAsync(
        Guid tenantId,
        Guid branchId,
        Guid eventId,
        string eventType,
        string aggregateId,
        string payloadJson,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct = default);
}
