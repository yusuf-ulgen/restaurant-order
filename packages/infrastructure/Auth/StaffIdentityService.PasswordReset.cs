using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
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

        var membership = await _bootstrapGateway.LookupFirstActiveMembershipByUserIdAsync(lookupUser.UserId, ct);
        if (membership == null || membership.TenantId == Guid.Empty)
        {
            return null; // Non-enumerating: SuperAdmin or users without active tenant membership
        }
        var tenantId = TenantId.From(membership.TenantId);

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = ComputeSha256(rawToken);

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(tenantId.Value, cancellationToken: ct);
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

        var tokenDto = await _bootstrapGateway.LookupPasswordResetTokenAsync(tokenHash, ct);
        if (tokenDto == null || tokenDto.IsConsumed || tokenDto.ExpiresAtUtc <= now)
        {
            throw new InvalidOperationException("Invalid, expired, or already consumed password reset token.");
        }

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx && tokenDto.TenantId != Guid.Empty)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(tokenDto.TenantId, cancellationToken: ct);
        }

        try
        {
            var resetToken = await _dbContext.PasswordResetTokens
                .FirstOrDefaultAsync(t => t.Id == tokenDto.ResetTokenId, ct);
            if (resetToken == null || !resetToken.IsValid(now))
            {
                throw new InvalidOperationException("Invalid, expired, or already consumed password reset token.");
            }
            resetToken.Consume(now);

            var passwordHash = _passwordHasher.HashPassword(command.NewPassword).Hash;
            await _bootstrapGateway.ResetUserPasswordAsync(tokenDto.UserId, passwordHash, now, ct);

            var audit = SecurityAuditEvent.Create(
                TenantId.From(tokenDto.TenantId),
                SecurityAuditEventType.PasswordChanged,
                now,
                UserId.From(tokenDto.UserId),
                detailsJson: "{\"action\":\"password_reset_completed\"}");
            _dbContext.SecurityAuditEvents.Add(audit);

            await _dbContext.SaveChangesAsync(ct);
            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }
        }
        finally
        {
            if (localTx != null)
            {
                await localTx.DisposeAsync();
            }
        }
    }
}
