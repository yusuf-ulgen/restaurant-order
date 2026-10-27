using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Events;

/// <summary>
/// Background operational outbox dispatcher.
/// Claims batches using a secure claim gateway (PostgreSQL function with FOR UPDATE SKIP LOCKED),
/// guarantees claim ownership validation, supports mid-batch lease verification,
/// safely handles cancellations without corrupting attempt counters,
/// and dead-letters poison payloads.
/// </summary>
public sealed class OperationalOutboxDispatcher : IOperationalOutboxDispatcher
{
    private const int DefaultLeaseSeconds = 60;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly Regex SensitivePatterns = new(
        @"(?i)(password|secret|token|key|pwd|connectionstring)\s*[:=]\s*([^\s;,]+)",
        RegexOptions.Compiled);

    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IRealtimeEventPublisher _eventPublisher;
    private readonly RestaurantOrder.Application.Notifications.IPushNotificationDeliveryStager? _deliveryStager;
    private readonly ILogger<OperationalOutboxDispatcher> _logger;

    public OperationalOutboxDispatcher(
        RestaurantOrderDbContext dbContext,
        IRealtimeEventPublisher eventPublisher,
        ILogger<OperationalOutboxDispatcher> logger,
        RestaurantOrder.Application.Notifications.IPushNotificationDeliveryStager? deliveryStager = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deliveryStager = deliveryStager;
    }

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
        int MaxAttempts,
        string ClaimId);

    public async Task<int> DispatchPendingBatchAsync(
        int batchSize = 25,
        Func<CancellationToken, Task<bool>>? isLeaseValid = null,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return 0;
        }
        if (isLeaseValid != null && !await isLeaseValid(ct)) return 0;

        if (batchSize <= 0) batchSize = 25;

        var nowUtc = DateTimeOffset.UtcNow;
        var claimId = $"dispatcher-{Guid.NewGuid():N}";
        var lockExpiresUtc = nowUtc.AddSeconds(DefaultLeaseSeconds);

        var claimedItems = new List<ClaimedOutboxRecord>();
        var isPostgres = _dbContext.Database.IsNpgsql();
        DbConnection? conn = null;

        try
        {
            if (isPostgres)
            {
                conn = _dbContext.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync(ct);
                }

                await using var claimCmd = conn.CreateCommand();
                claimCmd.CommandText = @"
                    SELECT id, tenant_id, branch_id, aggregate_id, event_type, payload, schema_version, sequence_number, occurred_at, attempt_count, max_attempts, lock_claim_id
                    FROM tenancy.claim_pending_operational_outbox(@limit, @nowUtc, @leaseSeconds, @claimId, @tenantId);";

                AddParameter(claimCmd, "limit", batchSize);
                AddParameter(claimCmd, "nowUtc", nowUtc);
                AddParameter(claimCmd, "leaseSeconds", DefaultLeaseSeconds);
                AddParameter(claimCmd, "claimId", claimId);
                AddParameter(claimCmd, "tenantId", (object?)tenantId ?? DBNull.Value);

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
                        MaxAttempts: reader.GetInt32(10),
                        ClaimId: reader.GetString(11)));
                }
            }
            else
            {
                // Fallback for non-Postgres / InMemory / SQLite test providers
                var query = _dbContext.OperationalOutbox
                    .IgnoreQueryFilters()
                    .Where(o => (o.Status == OperationalOutboxStatus.Pending && (o.NextAttemptUtc == null || o.NextAttemptUtc <= nowUtc)) ||
                                (o.Status == OperationalOutboxStatus.Processing && o.LockExpiresUtc != null && o.LockExpiresUtc < nowUtc));

                if (tenantId.HasValue)
                {
                    var tid = new TenantId(tenantId.Value);
                    query = query.Where(o => o.TenantId == tid);
                }

                var pendingEntities = await query
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
                        entity.MaxAttempts,
                        claimId));
                }

                if (claimedItems.Count > 0)
                {
                    await _dbContext.SaveChangesAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
        }

        if (claimedItems.Count == 0)
        {
            return 0;
        }

        var dispatchedCount = 0;

        foreach (var item in claimedItems)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogInformation("Operational outbox dispatch batch cancelled.");
                break;
            }

            if (isLeaseValid != null)
            {
                var valid = await isLeaseValid(ct);
                if (!valid)
                {
                    _logger.LogWarning("Leadership lease or active slot lost during outbox dispatch. Aborting remaining batch.");
                    break;
                }
            }

            try
            {
                await using var work = await WorkerClaimTransaction.BeginAsync(_dbContext, item.TenantId,
                    item.Id, claimId, push: false, DefaultLeaseSeconds, ct);
                if (!work.IsValid) continue;
                string envelopeJson;
                try
                {
                    using var doc = JsonDocument.Parse(item.Payload);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new JsonException("Outbox payload root must be a JSON object.");
                    }

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
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "Poison payload detected for outbox message {Id}. Marking DeadLetter immediately.", item.Id);
                    var poisonError = SanitizeErrorMessage("Poison payload: " + jsonEx.Message);
                    await RecordFailureAsync(conn, item, claimId, poisonError, isDeadLetter: true, ct);
                    await work.CommitAsync(ct);
                    continue;
                }

                await _eventPublisher.PublishBroadcastAsync(envelopeJson, ct);

                if (_deliveryStager != null)
                {
                        await _deliveryStager.StageDeliveriesForEventAsync(
                            item.TenantId,
                            item.BranchId,
                            item.Id,
                            item.EventType,
                            item.AggregateId,
                            item.Payload,
                            item.OccurredAtUtc,
                            ct);
                }

                if (isPostgres && conn != null)
                {
                    work.EnsureValid(ct);
                    if (isLeaseValid != null && !await isLeaseValid(ct)) break;
                    await using var completeCmd = conn.CreateCommand();
                    completeCmd.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
                    completeCmd.CommandText = "SELECT tenancy.complete_operational_outbox(@id, @claimId, @nowUtc);";
                    AddParameter(completeCmd, "id", item.Id);
                    AddParameter(completeCmd, "claimId", claimId);
                    AddParameter(completeCmd, "nowUtc", DateTimeOffset.UtcNow);

                    var completeResult = await completeCmd.ExecuteScalarAsync(ct);
                    if (completeResult is not bool completed || !completed)
                    {
                        _logger.LogWarning(
                            "Outbox message {Id} completion skipped: claim was expired or claimed by another worker.",
                            item.Id);
                        continue;
                    }
                }
                else
                {
                    var entity = await _dbContext.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == item.Id, ct);
                    if (entity != null && entity.LockClaimId == claimId && entity.Status == OperationalOutboxStatus.Processing && entity.LockExpiresUtc > DateTimeOffset.UtcNow)
                    {
                        entity.MarkDispatched(DateTimeOffset.UtcNow);
                        await _dbContext.SaveChangesAsync(ct);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Outbox message {Id} completion skipped in fallback: claim mismatch or expired.",
                            item.Id);
                        continue;
                    }
                }

                await work.CommitAsync(ct);
                dispatchedCount++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("Dispatch cancelled mid-flight for message {Id}. Not marking as failure.", item.Id);
                break;
            }
            catch (Exception ex)
            {
                var isDeadLetter = item.AttemptCount >= item.MaxAttempts;
                var sanitizedError = SanitizeErrorMessage(ex.Message);

                _logger.LogWarning(ex,
                    "Failed to dispatch outbox event '{Id}'. Attempt: {Attempt}/{Max}. DeadLetter: {IsDeadLetter}.",
                    item.Id, item.AttemptCount, item.MaxAttempts, isDeadLetter);

                try
                {
                    await RecordFailureAsync(conn, item, claimId, sanitizedError, isDeadLetter, CancellationToken.None);
                }
                catch (Exception persistEx)
                {
                    _logger.LogError(persistEx, "Failed to persist failure status for outbox message {Id}.", item.Id);
                }
            }
        }

        return dispatchedCount;
    }

    private async Task RecordFailureAsync(
        DbConnection? conn,
        ClaimedOutboxRecord item,
        string claimId,
        string errorMessage,
        bool isDeadLetter,
        CancellationToken ct)
    {
        var delaySeconds = Math.Min(60, (int)Math.Pow(2, Math.Min(item.AttemptCount, 6)));
        var nextAttempt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);

        if (_dbContext.Database.IsNpgsql() && conn != null)
        {
            await using var failCmd = conn.CreateCommand();
            failCmd.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
            failCmd.CommandText = "SELECT tenancy.fail_operational_outbox(@id, @claimId, @error, @nextAttemptUtc, @isDeadLetter, @nowUtc);";
            AddParameter(failCmd, "id", item.Id);
            AddParameter(failCmd, "claimId", claimId);
            AddParameter(failCmd, "error", errorMessage);
            AddParameter(failCmd, "nextAttemptUtc", (object?)nextAttempt ?? DBNull.Value);
            AddParameter(failCmd, "isDeadLetter", isDeadLetter);
            AddParameter(failCmd, "nowUtc", DateTimeOffset.UtcNow);

            await failCmd.ExecuteScalarAsync(ct);
        }
        else
        {
            var entity = await _dbContext.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == item.Id, ct);
            if (entity != null && entity.LockClaimId == claimId && entity.Status == OperationalOutboxStatus.Processing)
            {
                entity.MarkFailed(errorMessage, nextAttempt, isDeadLetter);
                await _dbContext.SaveChangesAsync(ct);
            }
        }
    }

    internal static string SanitizeErrorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown dispatch error.";
        var sanitized = SensitivePatterns.Replace(message, "$1=***");
        return sanitized.Length > 490 ? sanitized[..490] : sanitized;
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        cmd.Parameters.Add(param);
    }
}
