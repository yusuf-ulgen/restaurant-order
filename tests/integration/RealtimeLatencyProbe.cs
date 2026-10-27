using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using RestaurantOrder.Api.Realtime;
using RestaurantOrder.Application.Events;

namespace RestaurantOrder.IntegrationTests;

internal sealed class RealtimeLatencyProbe
{
    internal sealed record PublishSpan(long Start, long End);
    internal sealed record AuthorizationSpan(string ConnectionId, long Start, long End);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PublishSpan>> _publishes = new();
    internal ConcurrentQueue<AuthorizationSpan> Authorizations { get; } = new();

    internal Task<PublishSpan> PublishedAsync(Guid id) => _publishes.GetOrAdd(id,
        _ => new TaskCompletionSource<PublishSpan>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    internal void RecordPublish(Guid id, long start, long end) => _publishes.GetOrAdd(id,
        _ => new TaskCompletionSource<PublishSpan>(TaskCreationOptions.RunContinuationsAsynchronously))
        .TrySetResult(new(start, end));
}

internal sealed class TimedRealtimePublisher(IRealtimeEventPublisher inner, RealtimeLatencyProbe probe) : IRealtimeEventPublisher
{
    public async Task PublishBroadcastAsync(string payload, CancellationToken ct = default)
    {
        using var json = JsonDocument.Parse(payload);
        var id = json.RootElement.GetProperty("event_id").GetGuid();
        var start = Stopwatch.GetTimestamp();
        await inner.PublishBroadcastAsync(payload, ct);
        probe.RecordPublish(id, start, Stopwatch.GetTimestamp());
    }

    public Task PublishSessionInvalidatedAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken ct = default) =>
        inner.PublishSessionInvalidatedAsync(tenantId, branchId, sessionId, ct);

    public Task PublishUserInvalidatedAsync(Guid tenantId, Guid userId, Guid? sessionId = null, string reason = "revoked", CancellationToken ct = default) =>
        inner.PublishUserInvalidatedAsync(tenantId, userId, sessionId, reason, ct);
}

internal sealed class TimedRealtimeAuthorizer(IRealtimeConnectionAuthorizer inner, RealtimeLatencyProbe probe)
    : IRealtimeConnectionAuthorizer
{
    public async Task<bool> IsAuthorizedAsync(TrackedConnection connection, CancellationToken ct = default)
    {
        var start = Stopwatch.GetTimestamp();
        try { return await inner.IsAuthorizedAsync(connection, ct); }
        finally { probe.Authorizations.Enqueue(new(connection.ConnectionId, start, Stopwatch.GetTimestamp())); }
    }
}
