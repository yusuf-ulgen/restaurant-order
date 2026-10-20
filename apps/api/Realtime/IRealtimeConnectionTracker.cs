using System;
using System.Collections.Generic;

namespace RestaurantOrder.Api.Realtime;

public sealed record TrackedConnection(
    string ConnectionId,
    Guid TenantId,
    Guid BranchId,
    string Role,
    Guid? DiningSessionId,
    Guid? UserId);

/// <summary>
/// Thread-safe tracker of active SignalR connections and their security context.
/// Enables continuous authorization, token revocation enforcement, and session closure evictions.
/// </summary>
public interface IRealtimeConnectionTracker
{
    void Track(TrackedConnection connection);
    void Untrack(string connectionId);
    IReadOnlyList<TrackedConnection> GetConnectionsForSession(Guid tenantId, Guid branchId, Guid diningSessionId);
    IReadOnlyList<TrackedConnection> GetConnectionsForUser(Guid tenantId, Guid userId);
}
