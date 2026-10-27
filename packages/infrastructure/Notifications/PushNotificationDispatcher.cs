using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Notifications;

public sealed class PushNotificationDispatcher : IPushNotificationDispatcher
{
    private const int DefaultLeaseSeconds = 30;

    private static readonly Regex SensitivePatterns = new(
        @"(?i)(password|secret|token|key|pwd|connectionstring)\s*[:=]\s*([^\s;,]+)",
        RegexOptions.Compiled);

    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IPushNotificationTransport _pushTransport;
    private readonly ICustomerSessionValidator _sessionValidator;
    private readonly ILogger<PushNotificationDispatcher> _logger;

    public PushNotificationDispatcher(
        RestaurantOrderDbContext dbContext,
        IPushNotificationTransport pushTransport,
        ICustomerSessionValidator sessionValidator,
        ILogger<PushNotificationDispatcher> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _pushTransport = pushTransport ?? throw new ArgumentNullException(nameof(pushTransport));
        _sessionValidator = sessionValidator ?? throw new ArgumentNullException(nameof(sessionValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private sealed record ClaimedDeliveryRecord(
        Guid Id,
        Guid TenantId,
        Guid BranchId,
        Guid EventId,
        string EventType,
        Guid DeviceRegistrationId,
        string InstallationId,
        string RecipientType,
        Guid RecipientId,
        int AttemptCount,
        int MaxAttempts,
        DateTimeOffset ExpiresAtUtc,
        string ClaimId);

    public async Task<int> DispatchPendingBatchAsync(
        int batchSize = 25,
        Func<CancellationToken, Task<bool>>? isLeaseValid = null,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return 0;
        if (isLeaseValid != null && !await isLeaseValid(ct)) return 0;
        if (batchSize <= 0) batchSize = 25;

        var nowUtc = DateTimeOffset.UtcNow;
        var claimId = $"push-dispatcher-{Guid.NewGuid():N}";
        var lockExpiresUtc = nowUtc.AddSeconds(DefaultLeaseSeconds);

        var claimedItems = new List<ClaimedDeliveryRecord>();
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
                    SELECT id, tenant_id, branch_id, event_id, event_type, device_registration_id,
                           installation_id, recipient_type, recipient_id, attempt_count, max_attempts,
                           expires_at, lock_claim_id
                    FROM tenancy.claim_pending_notification_deliveries(@limit, @nowUtc, @leaseSeconds, @claimId, @tenantId);";

                AddParameter(claimCmd, "limit", batchSize);
                AddParameter(claimCmd, "nowUtc", nowUtc);
                AddParameter(claimCmd, "leaseSeconds", DefaultLeaseSeconds);
                AddParameter(claimCmd, "claimId", claimId);
                AddParameter(claimCmd, "tenantId", (object?)tenantId ?? DBNull.Value);

                await using var reader = await claimCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    claimedItems.Add(new ClaimedDeliveryRecord(
                        Id: reader.GetGuid(0),
                        TenantId: reader.GetGuid(1),
                        BranchId: reader.GetGuid(2),
                        EventId: reader.GetGuid(3),
                        EventType: reader.GetString(4),
                        DeviceRegistrationId: reader.GetGuid(5),
                        InstallationId: reader.GetString(6),
                        RecipientType: reader.GetString(7),
                        RecipientId: reader.GetGuid(8),
                        AttemptCount: reader.GetInt32(9),
                        MaxAttempts: reader.GetInt32(10),
                        ExpiresAtUtc: reader.GetFieldValue<DateTimeOffset>(11),
                        ClaimId: reader.GetString(12)));
                }
            }
            else
            {
                // Fallback for InMemory / SQLite
                var query = _dbContext.NotificationDeliveries
                    .IgnoreQueryFilters()
                    .Where(d => (d.Status == NotificationDeliveryStatus.Pending && (d.NextAttemptUtc == null || d.NextAttemptUtc <= nowUtc)) ||
                                 (d.Status == NotificationDeliveryStatus.FailedRetryable && (d.NextAttemptUtc == null || d.NextAttemptUtc <= nowUtc)) ||
                                 (d.Status == NotificationDeliveryStatus.Claimed && d.LockExpiresUtc != null && d.LockExpiresUtc < nowUtc));

                if (tenantId.HasValue)
                {
                    var tid = new TenantId(tenantId.Value);
                    query = query.Where(d => d.TenantId == tid);
                }

                var pendingEntities = await query
                    .OrderBy(d => d.CreatedAtUtc)
                    .Take(batchSize)
                    .ToListAsync(ct);

                foreach (var entity in pendingEntities)
                {
                    entity.Claim(claimId, lockExpiresUtc);
                    claimedItems.Add(new ClaimedDeliveryRecord(
                        entity.Id,
                        entity.TenantId.Value,
                        entity.BranchId.Value,
                        entity.EventId,
                        entity.EventType,
                        entity.DeviceRegistrationId,
                        entity.InstallationId,
                        entity.RecipientType,
                        entity.RecipientId,
                        entity.AttemptCount,
                        entity.MaxAttempts,
                        entity.ExpiresAtUtc,
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

        if (claimedItems.Count == 0) return 0;

        var dispatchedCount = 0;

        foreach (var item in claimedItems)
        {
            if (ct.IsCancellationRequested) break;

            if (isLeaseValid != null)
            {
                var valid = await isLeaseValid(ct);
                if (!valid)
                {
                    _logger.LogWarning("Worker leadership lost during push dispatching. Halting remaining batch.");
                    break;
                }
            }

            await using var work = await WorkerClaimTransaction.BeginAsync(_dbContext, item.TenantId,
                item.Id, claimId, push: true, DefaultLeaseSeconds, ct);
            if (!work.IsValid) continue;
            async Task PersistAsync(NotificationDelivery delivery)
            {
                work.EnsureValid(ct);
                if (isLeaseValid != null && !await isLeaseValid(ct)) throw new OperationCanceledException("Leadership lost.");
                if (!await _dbContext.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().AnyAsync(d =>
                    d.Id == item.Id && d.LockClaimId == claimId && d.Status == NotificationDeliveryStatus.Claimed &&
                    d.LockExpiresUtc > DateTimeOffset.UtcNow, ct)) throw new OperationCanceledException("Push claim lost.");
                if (isPostgres)
                {
                    var updated = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE tenancy.notification_deliveries
                        SET status = {(int)delivery.Status}, next_attempt_utc = {delivery.NextAttemptUtc},
                            processed_at = {delivery.ProcessedAtUtc}, last_error = {delivery.LastError},
                            lock_claim_id = NULL, lock_expires_utc = NULL
                        WHERE id = {item.Id} AND tenant_id = {item.TenantId} AND status = 1
                          AND lock_claim_id = {claimId} AND lock_expires_utc > clock_timestamp()
                        """, ct);
                    if (updated != 1) throw new OperationCanceledException("Push claim expired before persistence.");
                    _dbContext.Entry(delivery).State = EntityState.Unchanged;
                }
                await _dbContext.SaveChangesAsync(ct);
                await work.CommitAsync(ct);
            }
            var itemNow = DateTimeOffset.UtcNow;
            var deliveryEntity = await _dbContext.NotificationDeliveries
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.Id == item.Id, ct);

            if (deliveryEntity == null || deliveryEntity.LockClaimId != claimId ||
                deliveryEntity.Status != NotificationDeliveryStatus.Claimed || deliveryEntity.LockExpiresUtc <= itemNow) continue;

            // 1. Short Event TTL check: EventId remains constant across retries
            if (deliveryEntity.ExpiresAtUtc <= itemNow)
            {
                deliveryEntity.MarkExpired(itemNow);
                await PersistAsync(deliveryEntity);
                continue;
            }

            // 2. Device Registration active check
            if (isPostgres)
                await _dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM tenancy.device_registrations WHERE id = {item.DeviceRegistrationId} AND tenant_id = {item.TenantId} FOR UPDATE", ct);
            var device = await _dbContext.DeviceRegistrations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.Id == item.DeviceRegistrationId, ct);

            var sameOwner = device != null && device.TenantId == deliveryEntity.TenantId &&
                device.BranchId == deliveryEntity.BranchId && device.InstallationId == item.InstallationId &&
                (item.RecipientType == "Customer" ? device.DiningSessionId?.Value == item.RecipientId && device.UserId == null :
                    device.UserId?.Value == item.RecipientId && device.DiningSessionId == null);
            if (!sameOwner || device == null || !device.IsActive || device.ExpiresAtUtc <= itemNow)
            {
                deliveryEntity.MarkPermanentFailure("Device registration inactive or expired.", itemNow);
                await PersistAsync(deliveryEntity);
                continue;
            }

            // 3. Pre-send continuous authorization verification
            if (string.Equals(item.RecipientType, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                var isSessionActive = await _sessionValidator.ValidateSessionActiveAsync(
                    item.TenantId, item.BranchId, item.RecipientId, ct);

                if (!isSessionActive)
                {
                    // Session closed: revoke device and fail delivery
                    device.Deactivate(itemNow);
                    deliveryEntity.MarkPermanentFailure("Customer dining session is closed.", itemNow);
                    await PersistAsync(deliveryEntity);
                    continue;
                }
            }
            else
            {
                // Staff recipient: check active membership and permissions
                var membership = await _dbContext.Memberships
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.TenantId == deliveryEntity.TenantId &&
                                              m.UserId == new UserId(item.RecipientId) &&
                                              (m.BranchId == deliveryEntity.BranchId || m.BranchId == null), ct);

                var recipientUser = new UserId(item.RecipientId);
                var activeUser = await _dbContext.Users.AsNoTracking().AnyAsync(u => u.Id == recipientUser && u.Status == UserStatus.Active, ct);
                if (!activeUser || membership == null || membership.Status != UserMembershipStatus.Active ||
                    !CanReceiveEvent(membership.Role, item.EventType))
                {
                    deliveryEntity.MarkPermanentFailure("Staff membership is inactive, suspended, or revoked.", itemNow);
                    await PersistAsync(deliveryEntity);
                    continue;
                }
            }

            // 4. Send via IPushNotificationTransport
            PushNotificationRequest pushRequest;
            try
            {
                pushRequest = new PushNotificationRequest(item.InstallationId, item.EventId, item.ExpiresAtUtc);
            }
            catch (Exception ex)
            {
                deliveryEntity.MarkPermanentFailure(SanitizeErrorMessage("Invalid push request: " + ex.Message), itemNow);
                await PersistAsync(deliveryEntity);
                continue;
            }

            PushSendResult result;
            try
            {
                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                sendTimeout.CancelAfter(TimeSpan.FromSeconds(DefaultLeaseSeconds - 5));
                result = await _pushTransport.SendAsync(pushRequest, sendTimeout.Token);
                work.EnsureValid(ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result = new PushSendResult(PushSendStatus.RetryableFailure);
            }
            catch (Exception ex)
            {
                var safeError = SanitizeErrorMessage(ex.Message);
                result = new PushSendResult(PushSendStatus.RetryableFailure);
                deliveryEntity.MarkRetryableFailure(safeError, TimeSpan.FromSeconds(30), itemNow);
                await PersistAsync(deliveryEntity);
                continue;
            }

            // 5. Handle provider result
            switch (result.Status)
            {
                case PushSendStatus.ProviderAccepted:
                case PushSendStatus.Disabled:
                    deliveryEntity.MarkProviderAccepted(itemNow);
                    dispatchedCount++;
                    break;

                case PushSendStatus.RetryableFailure:
                    var delay = result.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, deliveryEntity.AttemptCount) * 5));
                    deliveryEntity.MarkRetryableFailure("Provider retryable failure.", delay, itemNow);
                    break;

                case PushSendStatus.InvalidRecipient:
                    // Deactivate device registration safely so future events don't target invalid device
                    device.Deactivate(itemNow);
                    deliveryEntity.MarkPermanentFailure("Invalid recipient token.", itemNow);
                    break;

                case PushSendStatus.ConfigurationFailure:
                    deliveryEntity.MarkPermanentFailure("Cloud push configuration failure.", itemNow);
                    break;

                case PushSendStatus.PermanentFailure:
                    deliveryEntity.MarkPermanentFailure("Permanent delivery failure.", itemNow);
                    break;

                case PushSendStatus.Expired:
                    deliveryEntity.MarkExpired(itemNow);
                    break;
            }

            await PersistAsync(deliveryEntity);
        }

        return dispatchedCount;
    }

    private static void AddParameter(DbCommand cmd, string name, object? value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }

    private static bool CanReceiveEvent(AuthRole role, string eventType) => role switch
    {
        AuthRole.SuperAdmin or AuthRole.RestaurantAdmin or AuthRole.BranchManager => true,
        AuthRole.Waiter or AuthRole.Cashier => true,
        AuthRole.Kitchen or AuthRole.Bar => eventType is "order.created" or "order.item_status_changed" or "menu.item_availability_changed",
        _ => false
    };

    private static string SanitizeErrorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown error";
        return SensitivePatterns.Replace(message, "$1:[REDACTED]");
    }
}
