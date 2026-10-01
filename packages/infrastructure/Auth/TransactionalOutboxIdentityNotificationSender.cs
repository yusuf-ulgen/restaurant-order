using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Transactional outbox implementation of <see cref="IIdentityNotificationSender"/>.
/// Enqueues encrypted notification messages into iam.identity_notifications_outbox
/// within the caller's active database transaction, guaranteeing transactional consistency
/// and rollback safety with zero plaintext token persistence.
/// </summary>
public sealed class TransactionalOutboxIdentityNotificationSender : IIdentityNotificationSender
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IIdentityOutboxPayloadProtector _payloadProtector;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<TransactionalOutboxIdentityNotificationSender> _logger;

    public TransactionalOutboxIdentityNotificationSender(
        RestaurantOrderDbContext dbContext,
        IIdentityOutboxPayloadProtector payloadProtector,
        ITenantContext tenantContext,
        ILogger<TransactionalOutboxIdentityNotificationSender> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _payloadProtector = payloadProtector ?? throw new ArgumentNullException(nameof(payloadProtector));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendInvitationAsync(
        string email,
        string invitationToken,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var now = DateTimeOffset.UtcNow;
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invitationToken))).ToLowerInvariant();
        var idempotencyKey = $"invitation:{tokenHash}";

        var payload = new
        {
            email,
            invitationToken,
            expiresAtUtc,
            enqueuedAtUtc = now
        };

        var rawJson = JsonSerializer.Serialize(payload);
        var encrypted = _payloadProtector.Protect(rawJson);

        var message = IdentityNotificationOutboxMessage.Create(
            tenantId,
            "StaffInvitation",
            email,
            encrypted,
            idempotencyKey,
            now);

        _dbContext.IdentityNotificationOutbox.Add(message);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Enqueued staff invitation notification in transactional outbox for '{Email}'.", email);
    }

    public async Task SendPasswordResetAsync(
        string email,
        string resetToken,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var now = DateTimeOffset.UtcNow;
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resetToken))).ToLowerInvariant();
        var idempotencyKey = $"password_reset:{tokenHash}";

        var payload = new
        {
            email,
            resetToken,
            expiresAtUtc,
            enqueuedAtUtc = now
        };

        var rawJson = JsonSerializer.Serialize(payload);
        var encrypted = _payloadProtector.Protect(rawJson);

        var message = IdentityNotificationOutboxMessage.Create(
            tenantId,
            "PasswordReset",
            email,
            encrypted,
            idempotencyKey,
            now);

        _dbContext.IdentityNotificationOutbox.Add(message);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Enqueued password reset notification in transactional outbox for '{Email}'.", email);
    }

    private TenantId ResolveTenantId()
    {
        if (_dbContext.HasTenant && _dbContext.CurrentTenantId.Value != Guid.Empty)
        {
            return _dbContext.CurrentTenantId;
        }

        if (_tenantContext.HasTenant && _tenantContext.TenantId.HasValue && _tenantContext.TenantId.Value != Guid.Empty)
        {
            return new TenantId(_tenantContext.TenantId.Value);
        }

        throw new InvalidOperationException("Active tenant context is required to enqueue transactional outbox notifications.");
    }
}
