using System.Text.Json;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Events;

/// <summary>
/// Infrastructure implementation of <see cref="IOperationalOutboxWriter"/>.
/// Enqueues events into tenancy.operational_outbox within the caller's active database transaction.
/// </summary>
public sealed class OperationalOutboxWriter : IOperationalOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public OperationalOutboxWriter(
        RestaurantOrderDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void EnqueueEvent<T>(
        Guid tenantId,
        Guid branchId,
        string aggregateId,
        string eventType,
        T data,
        string idempotencyKey,
        DateTimeOffset? occurredAtUtc = null,
        int schemaVersion = 1,
        Guid? eventId = null)
    {
        var now = _timeProvider.GetUtcNow();
        var occurred = occurredAtUtc ?? now;
        var payloadJson = JsonSerializer.Serialize(data, JsonOptions);

        var message = OperationalOutboxMessage.Create(
            tenantId: new TenantId(tenantId),
            branchId: new BranchId(branchId),
            aggregateId: aggregateId,
            eventType: eventType,
            payload: payloadJson,
            idempotencyKey: idempotencyKey,
            occurredAtUtc: occurred,
            createdAtUtc: now,
            schemaVersion: schemaVersion,
            maxAttempts: 5,
            id: eventId);

        _dbContext.OperationalOutbox.Add(message);
    }
}
