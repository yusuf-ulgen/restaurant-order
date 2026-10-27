using System;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantOrder.Application.Events;

/// <summary>
/// Service to publish real-time domain events to the distributed backplane (Redis).
/// Decouples event dispatching workers from API SignalR hub instances.
/// </summary>
public interface IRealtimeEventPublisher
{
    Task PublishBroadcastAsync(string eventPayloadJson, CancellationToken ct = default);
    Task PublishSessionInvalidatedAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken ct = default);
    Task PublishUserInvalidatedAsync(Guid tenantId, Guid userId, Guid? sessionId = null, string reason = "revoked", CancellationToken ct = default);
}
