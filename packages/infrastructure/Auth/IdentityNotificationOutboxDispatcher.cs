using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

public sealed record ClaimedOutboxItem(
    Guid Id,
    Guid TenantId,
    string NotificationType,
    string RecipientEmail,
    string EncryptedPayload,
    string IdempotencyKey,
    int AttemptCount,
    int MaxAttempts,
    Guid ClaimToken);

/// <summary>
/// Claims and dispatches pending identity notifications using PostgreSQL SKIP LOCKED concurrency.
/// Features lease-based claims to recover from worker crashes, payload decryption,
/// exponential backoff retries, and dead-lettering.
/// </summary>
public sealed class IdentityNotificationOutboxDispatcher : IIdentityNotificationOutboxDispatcher
{
    public const int DefaultLeaseSeconds = 60;

    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IIdentityOutboxPayloadProtector _payloadProtector;
    private readonly IIdentityNotificationTransport _transport;
    private readonly ILogger<IdentityNotificationOutboxDispatcher> _logger;

    public IdentityNotificationOutboxDispatcher(
        RestaurantOrderDbContext dbContext,
        IIdentityOutboxPayloadProtector payloadProtector,
        IIdentityNotificationTransport transport,
        ILogger<IdentityNotificationOutboxDispatcher> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _payloadProtector = payloadProtector ?? throw new ArgumentNullException(nameof(payloadProtector));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> DispatchPendingBatchAsync(int batchSize = 10, CancellationToken ct = default)
    {
        if (batchSize <= 0) batchSize = 10;

        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        var claimedItems = new List<ClaimedOutboxItem>();
        var claimNow = DateTimeOffset.UtcNow;
        var batchClaimToken = Guid.NewGuid();

        await using (var claimCmd = conn.CreateCommand())
        {
            claimCmd.CommandText = @"
                SELECT id, tenant_id, notification_type, recipient_email, encrypted_payload, idempotency_key, attempt_count, max_attempts, claim_token
                FROM iam.claim_pending_identity_notifications(@limit, @nowUtc, @leaseSeconds, @claimToken);";

            var pLimit = claimCmd.CreateParameter();
            pLimit.ParameterName = "limit";
            pLimit.Value = batchSize;
            claimCmd.Parameters.Add(pLimit);

            var pNow = claimCmd.CreateParameter();
            pNow.ParameterName = "nowUtc";
            pNow.Value = claimNow;
            claimCmd.Parameters.Add(pNow);

            var pLease = claimCmd.CreateParameter();
            pLease.ParameterName = "leaseSeconds";
            pLease.Value = DefaultLeaseSeconds;
            claimCmd.Parameters.Add(pLease);

            var pToken = claimCmd.CreateParameter();
            pToken.ParameterName = "claimToken";
            pToken.Value = batchClaimToken;
            claimCmd.Parameters.Add(pToken);

            await using var reader = await claimCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                claimedItems.Add(new ClaimedOutboxItem(
                    Id: reader.GetGuid(0),
                    TenantId: reader.GetGuid(1),
                    NotificationType: reader.GetString(2),
                    RecipientEmail: reader.GetString(3),
                    EncryptedPayload: reader.GetString(4),
                    IdempotencyKey: reader.GetString(5),
                    AttemptCount: reader.GetInt32(6),
                    MaxAttempts: reader.GetInt32(7),
                    ClaimToken: reader.GetGuid(8)));
            }
        }

        if (claimedItems.Count == 0)
        {
            return 0;
        }

        var successCount = 0;
        foreach (var item in claimedItems)
        {
            try
            {
                var plaintext = _payloadProtector.Unprotect(item.EncryptedPayload);
                await _transport.SendAsync(item.NotificationType, item.RecipientEmail, plaintext, item.IdempotencyKey, ct);

                await using var completeCmd = conn.CreateCommand();
                completeCmd.CommandText = "SELECT iam.complete_identity_notification(@id, @claimToken, @nowUtc);";

                var pId = completeCmd.CreateParameter();
                pId.ParameterName = "id";
                pId.Value = item.Id;
                completeCmd.Parameters.Add(pId);

                var pClaimToken = completeCmd.CreateParameter();
                pClaimToken.ParameterName = "claimToken";
                pClaimToken.Value = item.ClaimToken;
                completeCmd.Parameters.Add(pClaimToken);

                var pNow = completeCmd.CreateParameter();
                pNow.ParameterName = "nowUtc";
                pNow.Value = DateTimeOffset.UtcNow;
                completeCmd.Parameters.Add(pNow);

                var completeResult = await completeCmd.ExecuteScalarAsync(ct);
                if (completeResult is not bool completedSuccessfully || !completedSuccessfully)
                {
                    _logger.LogWarning(
                        "Notification outbox message {Id} was not marked completed because its lease expired or was reclaimed by another worker.",
                        item.Id);
                    continue;
                }

                successCount++;
                _logger.LogInformation(
                    "Successfully delivered outbox notification {Id} of type '{NotificationType}' to {RecipientEmail}.",
                    item.Id, item.NotificationType, MaskEmail(item.RecipientEmail));
            }
            catch (Exception ex)
            {
                var isDeadLetter = item.AttemptCount >= item.MaxAttempts;
                var backoffSeconds = Math.Min(300, Math.Pow(2, Math.Min(item.AttemptCount, 6)) * 5);
                var nextAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);

                var safeErrorMessage = ex.Message;
                if (safeErrorMessage.Length > 500)
                {
                    safeErrorMessage = safeErrorMessage[..500];
                }

                await using var failCmd = conn.CreateCommand();
                failCmd.CommandText = "SELECT iam.fail_identity_notification(@id, @claimToken, @error, @nextAttempt, @isDeadLetter, @nowUtc);";

                var pId = failCmd.CreateParameter();
                pId.ParameterName = "id";
                pId.Value = item.Id;
                failCmd.Parameters.Add(pId);

                var pClaimToken = failCmd.CreateParameter();
                pClaimToken.ParameterName = "claimToken";
                pClaimToken.Value = item.ClaimToken;
                failCmd.Parameters.Add(pClaimToken);

                var pErr = failCmd.CreateParameter();
                pErr.ParameterName = "error";
                pErr.Value = (object?)safeErrorMessage ?? DBNull.Value;
                failCmd.Parameters.Add(pErr);

                var pNext = failCmd.CreateParameter();
                pNext.ParameterName = "nextAttempt";
                pNext.Value = nextAttemptUtc;
                failCmd.Parameters.Add(pNext);

                var pDead = failCmd.CreateParameter();
                pDead.ParameterName = "isDeadLetter";
                pDead.Value = isDeadLetter;
                failCmd.Parameters.Add(pDead);

                var pNow = failCmd.CreateParameter();
                pNow.ParameterName = "nowUtc";
                pNow.Value = DateTimeOffset.UtcNow;
                failCmd.Parameters.Add(pNow);

                await failCmd.ExecuteScalarAsync(ct);

                _logger.LogError(
                    "Failed to deliver outbox notification {Id} of type '{NotificationType}' (Attempt {Attempt}/{MaxAttempts}). DeadLetter: {IsDeadLetter}. Error: {ErrorMessage}",
                    item.Id, item.NotificationType, item.AttemptCount, item.MaxAttempts, isDeadLetter, safeErrorMessage);
            }
        }

        return successCount;
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "***";
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return "***" + (atIndex >= 0 ? email[atIndex..] : "");
        return $"{email[0]}***{email[(atIndex - 1)..]}";
    }
}
