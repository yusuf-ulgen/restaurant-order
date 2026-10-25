using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace RestaurantOrder.Api.Realtime;

public sealed class RealtimeConnectionTracker : IRealtimeConnectionTracker
{
    private readonly ConcurrentDictionary<string, TrackedConnection> _connections = new();

    public void Track(TrackedConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connections[connection.ConnectionId] = connection;
    }

    public void Untrack(string connectionId)
    {
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            _connections.TryRemove(connectionId, out _);
        }
    }

    public IReadOnlyList<TrackedConnection> GetConnectionsForSession(Guid tenantId, Guid branchId, Guid diningSessionId)
    {
        return _connections.Values
            .Where(c => c.TenantId == tenantId && c.BranchId == branchId && c.DiningSessionId == diningSessionId)
            .ToList();
    }

    public IReadOnlyList<TrackedConnection> GetConnectionsForUser(Guid tenantId, Guid userId)
    {
        return _connections.Values
            .Where(c => c.TenantId == tenantId && c.UserId == userId)
            .ToList();
    }

    public IReadOnlyList<TrackedConnection> GetExpiredConnections(DateTimeOffset now)
    {
        return _connections.Values
            .Where(c => c.TokenExpiresAtUtc.HasValue && c.TokenExpiresAtUtc.Value <= now)
            .ToList();
    }

    public IReadOnlyList<TrackedConnection> GetAllConnections()
    {
        return _connections.Values.ToList();
    }
}
