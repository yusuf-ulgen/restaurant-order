using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Events;

public sealed class OperationalOutboxDispatcher : IOperationalOutboxDispatcher
{
    public const int DefaultLeaseSeconds = 30;

    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IRealtimeEventPublisher _eventPublisher;
    private readonly ILogger<OperationalOutboxDispatcher> _logger;

    public OperationalOutboxDispatcher(
        RestaurantOrderDbContext dbContext,
        IRealtimeEventPublisher eventPublisher,
        ILogger<OperationalOutboxDispatcher> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ClaimedOutboxRecord(
        Guid Id,
        Guid TenantId,
        Guid BranchId,
        string AggregateId,
        string EventType,
        string Payload,
        int SchemaVersion,
        long SequenceNumber,
        DateTimeOffset OccurredAtUtc,
        int AttemptCount,
        int MaxAttempts);

    public async Task<int> DispatchPendingBatchAsync(int batchSize = 25, CancellationToken ct = default)
    {
        if (batchSize <= 0) batchSize = 25;

        var nowUtc = DateTimeOffset.UtcNow;
        var claimId = $"dispatcher-{Guid.NewGuid():N}";
        var lockExpiresUtc = nowUtc.AddSeconds(DefaultLeaseSeconds);

        var claimedItems = new List<ClaimedOutboxRecord>();

        if (_dbContext.Database.IsNpgsql())
        {
            var conn = _dbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(ct);
            }

            await using var claimCmd = conn.CreateCommand();
            claimCmd.CommandText = @"
                WITH cte AS (
                    SELECT id
                    FROM tenancy.operational_outbox
                    WHERE (status = 1 AND (next_attempt_utc IS NULL OR next_attempt_utc <= @nowUtc))
                       OR (status = 2 AND lock_expires_utc < @nowUtc)
                    ORDER BY created_at_utc ASC
                    LIMIT @batchSize
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE tenancy.operational_outbox o
                SET status = 2,
                    lock_claim_id = @claimId,
                    lock_expires_utc = @lockExpiresUtc,
                    attempt_count = o.attempt_count + 1
                FROM cte
                WHERE o.id = cte.id
                RETURNING o.id, o.tenant_id, o.branch_id, o.aggregate_id, o.event_type, o.payload, o.schema_version, o.sequence_number, o.occurred_at_utc, o.attempt_count, o.max_attempts;";

            AddParameter(claimCmd, "nowUtc", nowUtc);
            AddParameter(claimCmd, "batchSize", batchSize);
            AddParameter(claimCmd, "claimId", claimId);
            AddParameter(claimCmd, "lockExpiresUtc", lockExpiresUtc);

            await using var reader = await claimCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                claimedItems.Add(new ClaimedOutboxRecord(
                    Id: reader.GetGuid(0),
                    TenantId: reader.GetGuid(1),
                    BranchId: reader.GetGuid(2),
                    AggregateId: reader.GetString(3),
                    EventType: reader.GetString(4),
                    Payload: reader.GetString(5),
                    SchemaVersion: reader.GetInt32(6),
                    SequenceNumber: reader.GetInt64(7),
                    OccurredAtUtc: reader.GetFieldValue<DateTimeOffset>(8),
                    AttemptCount: reader.GetInt32(9),
                    MaxAttempts: reader.GetInt32(10)));
            }
        }
        else
        {
            // Fallback for non-Postgres / InMemory / SQLite test providers
            var pendingEntities = await _dbContext.OperationalOutbox
                .IgnoreQueryFilters()
                .Where(o => (o.Status == OperationalOutboxStatus.Pending && (o.NextAttemptUtc == null || o.NextAttemptUtc <= nowUtc)) ||
                            (o.Status == OperationalOutboxStatus.Processing && o.LockExpiresUtc != null && o.LockExpiresUtc < nowUtc))
                .OrderBy(o => o.CreatedAtUtc)
                .Take(batchSize)
                .ToListAsync(ct);

            foreach (var entity in pendingEntities)
            {
                entity.Claim(claimId, lockExpiresUtc);
                claimedItems.Add(new ClaimedOutboxRecord(
                    entity.Id,
                    entity.TenantId.Value,
                    entity.BranchId.Value,
                    entity.AggregateId,
                    entity.EventType,
                    entity.Payload,
                    entity.SchemaVersion,
                    entity.SequenceNumber,
                    entity.OccurredAtUtc,
                    entity.AttemptCount + 1,
                    entity.MaxAttempts));
            }

            if (pendingEntities.Count > 0)
            {
                await _dbContext.SaveChangesAsync(ct);
            }
        }

        if (claimedItems.Count == 0)
        {
            return 0;
        }

        var dispatchedCount = 0;
        foreach (var item in claimedItems)
        {
            try
            {
                string envelopeJson;
                try
                {
                    using var doc = JsonDocument.Parse(item.Payload);
                    if (doc.RootElement.TryGetProperty("event_id", out _))
                    {
                        envelopeJson = item.Payload;
                    }
                    else
                    {
                        var envelope = new EventEnvelope<JsonElement>(
                            EventId: item.Id,
                            EventType: item.EventType,
                            SchemaVersion: item.SchemaVersion,
                            OccurredAtUtc: item.OccurredAtUtc,
                            TenantId: item.TenantId,
                            BranchId: item.BranchId,
                            AggregateId: item.AggregateId,
                            SequenceNumber: item.SequenceNumber,
                            Data: doc.RootElement.Clone());

                        envelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);
                    }
                }
                catch
                {
                    envelopeJson = item.Payload;
                }

                await _eventPublisher.PublishBroadcastAsync(envelopeJson, ct);

                if (_dbContext.Database.IsNpgsql())
                {
                    var conn = _dbContext.Database.GetDbConnection();
                    await using var updateCmd = conn.CreateCommand();
                    updateCmd.CommandText = @"
                        UPDATE tenancy.operational_outbox
                        SET status = 3,
                            dispatched_at_utc = @dispatchedAtUtc,
                            lock_claim_id = NULL,
                            lock_expires_utc = NULL
                        WHERE id = @id;";

                    AddParameter(updateCmd, "dispatchedAtUtc", DateTimeOffset.UtcNow);
                    AddParameter(updateCmd, "id", item.Id);
                    await updateCmd.ExecuteNonQueryAsync(ct);
                }
                else
                {
                    var entity = await _dbContext.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == item.Id, ct);
                    entity?.MarkDispatched(DateTimeOffset.UtcNow);
                    await _dbContext.SaveChangesAsync(ct);
                }

                dispatchedCount++;
            }
            catch (Exception ex)
            {
                var delaySeconds = Math.Min(60, (int)Math.Pow(2, Math.Min(item.AttemptCount, 6)));
                var isDeadLetter = item.AttemptCount >= item.MaxAttempts;
                var newStatus = isDeadLetter ? 4 : 1;
                var nextAttempt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
                var errorSnippet = ex.Message.Length > 490 ? ex.Message[..490] : ex.Message;

                _logger.LogWarning(ex,
                    "Failed to dispatch outbox event '{Id}'. Attempt: {Attempt}/{Max}. DeadLetter: {IsDeadLetter}.",
                    item.Id, item.AttemptCount, item.MaxAttempts, isDeadLetter);

                if (_dbContext.Database.IsNpgsql())
                {
                    var conn = _dbContext.Database.GetDbConnection();
                    await using var failCmd = conn.CreateCommand();
                    failCmd.CommandText = @"
                        UPDATE tenancy.operational_outbox
                        SET status = @status,
                            last_error = @lastError,
                            next_attempt_utc = @nextAttemptUtc,
                            lock_claim_id = NULL,
                            lock_expires_utc = NULL
                        WHERE id = @id;";

                    AddParameter(failCmd, "status", newStatus);
                    AddParameter(failCmd, "lastError", errorSnippet);
                    AddParameter(failCmd, "nextAttemptUtc", (object?)nextAttempt ?? DBNull.Value);
                    AddParameter(failCmd, "id", item.Id);
                    await failCmd.ExecuteNonQueryAsync(ct);
                }
                else
                {
                    var entity = await _dbContext.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == item.Id, ct);
                    entity?.MarkFailed(errorSnippet, nextAttempt, isDeadLetter);
                    await _dbContext.SaveChangesAsync(ct);
                }
            }
        }

        return dispatchedCount;
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        cmd.Parameters.Add(param);
    }
}
