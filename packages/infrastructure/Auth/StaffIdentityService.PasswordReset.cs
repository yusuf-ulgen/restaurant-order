using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Auth;

public sealed partial class StaffIdentityService
{
    public async Task<string?> RequestPasswordResetAsync(
        RequestPasswordResetCommand command,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedEmail = User.NormalizeEmail(command.Email);

        var lookupUser = await _userLookupGateway.LookupUserForLoginAsync(normalizedEmail, ct);
        if (lookupUser == null || lookupUser.Status != (int)UserStatus.Active)
        {
            return null; // Non-enumerating
        }

        Guid resolvedTenantId;
        if (!string.IsNullOrWhiteSpace(command.TenantSlug))
        {
            var tenant = await _bootstrapGateway.LookupTenantBySlugAsync(command.TenantSlug.Trim(), ct);
            if (tenant == null || tenant.Status != (int)TenantStatus.Active)
            {
                return null; // Non-enumerating
            }

            var membership = await _bootstrapGateway.LookupMembershipForLoginAsync(tenant.TenantId, lookupUser.UserId, ct);
            if (membership == null || !membership.IsActive)
            {
                return null; // Non-enumerating
            }

            resolvedTenantId = tenant.TenantId;
        }
        else
        {
            var activeMemberships = await _bootstrapGateway.LookupActiveMembershipsByUserIdAsync(lookupUser.UserId, ct);
            if (activeMemberships.Count == 0)
            {
                return null; // Non-enumerating
            }

            if (activeMemberships.Count > 1)
            {
                // Multi-tenant user without explicit TenantSlug: reject arbitrary selection
                return null; // Non-enumerating
            }

            resolvedTenantId = activeMemberships[0].TenantId;
        }

        var tenantId = TenantId.From(resolvedTenantId);
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = ComputeSha256(rawToken);

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(resolvedTenantId, cancellationToken: ct);
        }

        try
        {
            var resetToken = PasswordResetToken.Create(tenantId, UserId.From(lookupUser.UserId), tokenHash, DefaultResetTtl, now);
            _dbContext.PasswordResetTokens.Add(resetToken);

            var audit = SecurityAuditEvent.Create(
                tenantId,
                SecurityAuditEventType.PasswordReset,
                now,
                UserId.From(lookupUser.UserId),
                detailsJson: "{\"action\":\"reset_requested\"}");
            _dbContext.SecurityAuditEvents.Add(audit);

            await _dbContext.SaveChangesAsync(ct);

            // Send notification within transaction so failure rolls back reset token
            await _notificationSender.SendPasswordResetAsync(command.Email, rawToken, resetToken.ExpiresAtUtc, ct);

            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }

            return rawToken;
        }
        finally
        {
            if (localTx != null)
            {
                await localTx.DisposeAsync();
            }
        }
    }

    public async Task ResetPasswordAsync(
        ResetPasswordCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.ResetToken))
        {
            throw new ArgumentException("Reset token is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.NewPassword) || command.NewPassword.Length < 8)
        {
            throw new ArgumentException("New password must be at least 8 characters.", nameof(command));
        }

        var now = DateTimeOffset.UtcNow;
        var tokenHash = ComputeSha256(command.ResetToken.Trim().ToLowerInvariant());

        // 1. Look up token metadata before beginning transaction to resolve tenant ID
        var tokenLookup = await _bootstrapGateway.LookupPasswordResetTokenAsync(tokenHash, ct);
        if (tokenLookup == null || tokenLookup.IsConsumed || tokenLookup.ExpiresAtUtc <= now)
        {
            throw new InvalidOperationException("Invalid, expired, or already consumed password reset token.");
        }

        var tenantId = tokenLookup.TenantId;
        var userId = tokenLookup.UserId;
        var passwordHash = _passwordHasher.HashPassword(command.NewPassword).Hash;

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx && tenantId != Guid.Empty)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken: ct);
        }

        IReadOnlyList<Guid> revokedSessionIds = Array.Empty<Guid>();

        try
        {
            var dbConn = _dbContext.Database.GetDbConnection();
            var dbTx = _dbContext.Database.CurrentTransaction!.GetDbTransaction();

            // 2. Consume token atomically inside transaction
            var consumed = await _bootstrapGateway.ConsumePasswordResetTokenInTransactionAsync(
                dbConn,
                dbTx,
                tokenHash,
                now,
                ct);

            if (consumed == null)
            {
                throw new InvalidOperationException("Invalid, expired, or already consumed password reset token.");
            }

            // 3. Reset user password inside transaction
            await _bootstrapGateway.ResetUserPasswordInTransactionAsync(
                dbConn,
                dbTx,
                userId,
                passwordHash,
                now,
                ct);

            // 4. Revoke all user sessions and refresh tokens atomically in the same transaction
            revokedSessionIds = await _bootstrapGateway.RevokeAllUserSessionsInTransactionAsync(
                dbConn,
                dbTx,
                userId,
                now,
                ct);

            // 5. Security audit event
            var audit = SecurityAuditEvent.Create(
                TenantId.From(tenantId),
                SecurityAuditEventType.PasswordChanged,
                now,
                UserId.From(userId),
                detailsJson: "{\"action\":\"password_reset_completed\"}");
            _dbContext.SecurityAuditEvents.Add(audit);

            await _dbContext.SaveChangesAsync(ct);
            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }
        }
        catch
        {
            if (localTx != null)
            {
                await localTx.RollbackAsync(ct);
            }
            throw;
        }
        finally
        {
            if (localTx != null)
            {
                await localTx.DisposeAsync();
            }
        }

        // 6. Post-commit: Cache and platform session invalidations outside the transaction.
        // If cache invalidation or platform session cleanup fails, the persistent database
        // changes (password hash, security version, session and refresh token revocations) have already
        // successfully committed. We log safely without exposing secrets and do not throw.
        try
        {
            await _sessionManager.InvalidateUserCacheAsync(userId, ct);
            foreach (var sid in revokedSessionIds)
            {
                await _sessionManager.InvalidateSessionCacheAsync(sid, ct);
            }
            await _sessionManager.RevokePlatformSessionsAsync(UserId.From(userId), now);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Post-commit cache or platform session invalidation failed for user {UserId}. Database password update and session revocations were committed successfully.",
                userId);
        }
    }
}
