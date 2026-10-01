using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Implements staff identity lifecycle management: hierarchical invitations,
/// cryptographic single-use activation and reset tokens, and status transitions.
/// </summary>
public sealed class StaffIdentityService : IStaffIdentityService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private static readonly TimeSpan DefaultInvitationTtl = TimeSpan.FromDays(2);
    private static readonly TimeSpan DefaultResetTtl = TimeSpan.FromHours(1);

    public StaffIdentityService(
        RestaurantOrderDbContext dbContext,
        IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
    }

    public async Task<InviteStaffResult> InviteStaffAsync(
        TenantId? tenantId,
        InviteStaffCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        ValidateInviteAuthorization(actor, command.Role, tenantId, command.BranchId);

        var now = DateTimeOffset.UtcNow;
        var normalizedEmail = User.NormalizeEmail(command.Email);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);
        if (user == null)
        {
            var initialDummyHash = _passwordHasher.HashPassword(Guid.NewGuid().ToString("N")).Hash;
            user = User.Create(command.Email, initialDummyHash, now, initialStatus: UserStatus.Pending);
            _dbContext.Users.Add(user);
        }

        var effectiveTenantId = tenantId ?? (command.Role == AuthRole.SuperAdmin ? TenantId.From(Guid.Empty) : throw new InvalidOperationException("Tenant required."));
        var branchId = command.BranchId.HasValue ? new BranchId(command.BranchId.Value) : (BranchId?)null;

        var existingMembership = await _dbContext.Memberships
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.TenantId == effectiveTenantId && m.BranchId == branchId && m.IsActive, ct);

        if (existingMembership != null)
        {
            throw new InvalidOperationException("User already has an active membership with this scope.");
        }

        var membership = UserMembership.Create(effectiveTenantId, user.Id, command.Role, branchId, now);
        _dbContext.Memberships.Add(membership);

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = ComputeSha256(rawToken);

        var invitation = InvitationToken.Create(effectiveTenantId, user.Id, tokenHash, DefaultInvitationTtl, now);
        _dbContext.InvitationTokens.Add(invitation);

        var audit = SecurityAuditEvent.Create(
            effectiveTenantId,
            SecurityAuditEventType.RoleAssigned,
            now,
            user.Id,
            branchId,
            detailsJson: $"{{\"role\":\"{command.Role}\",\"invitedBy\":\"{actor.SubjectId}\"}}");
        _dbContext.SecurityAuditEvents.Add(audit);

        await _dbContext.SaveChangesAsync(ct);

        return new InviteStaffResult(
            UserId: user.Id.Value,
            Email: user.Email,
            Role: command.Role,
            BranchId: command.BranchId,
            InvitationToken: rawToken,
            ExpiresAtUtc: invitation.ExpiresAtUtc);
    }

    public async Task AcceptInvitationAsync(
        AcceptInvitationCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.InvitationToken))
        {
            throw new ArgumentException("Invitation token is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 8)
        {
            throw new ArgumentException("Password must be at least 8 characters.", nameof(command));
        }

        var now = DateTimeOffset.UtcNow;
        var tokenHash = ComputeSha256(command.InvitationToken.Trim().ToLowerInvariant());

        var invitation = await _dbContext.InvitationTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (invitation == null || !invitation.IsValid(now))
        {
            throw new InvalidOperationException("Invalid, expired, or already consumed invitation token.");
        }

        invitation.Consume(now);

        var user = await _dbContext.Users.FirstAsync(u => u.Id == invitation.UserId, ct);
        var passwordHash = _passwordHasher.HashPassword(command.Password).Hash;

        user.Activate(now);
        user.ChangePassword(passwordHash, now);

        var audit = SecurityAuditEvent.Create(
            invitation.TenantId,
            SecurityAuditEventType.PasswordChanged,
            now,
            user.Id,
            detailsJson: "{\"action\":\"invitation_accepted\"}");
        _dbContext.SecurityAuditEvents.Add(audit);

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<string?> RequestPasswordResetAsync(
        RequestPasswordResetCommand command,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedEmail = User.NormalizeEmail(command.Email);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);
        if (user == null || user.Status != UserStatus.Active)
        {
            return null; // Non-enumerating
        }

        var tenantId = TenantId.From(Guid.Empty);
        var membership = await _dbContext.Memberships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.IsActive, ct);

        if (membership != null)
        {
            tenantId = membership.TenantId;
        }

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = ComputeSha256(rawToken);

        var resetToken = PasswordResetToken.Create(tenantId, user.Id, tokenHash, DefaultResetTtl, now);
        _dbContext.PasswordResetTokens.Add(resetToken);

        var audit = SecurityAuditEvent.Create(
            tenantId,
            SecurityAuditEventType.PasswordReset,
            now,
            user.Id,
            detailsJson: "{\"action\":\"reset_requested\"}");
        _dbContext.SecurityAuditEvents.Add(audit);

        await _dbContext.SaveChangesAsync(ct);
        return rawToken;
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

        var resetToken = await _dbContext.PasswordResetTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (resetToken == null || !resetToken.IsValid(now))
        {
            throw new InvalidOperationException("Invalid, expired, or already consumed password reset token.");
        }

        resetToken.Consume(now);

        var user = await _dbContext.Users.FirstAsync(u => u.Id == resetToken.UserId, ct);
        var passwordHash = _passwordHasher.HashPassword(command.NewPassword).Hash;

        user.ChangePassword(passwordHash, now); // Automatically increments security version

        var audit = SecurityAuditEvent.Create(
            resetToken.TenantId,
            SecurityAuditEventType.PasswordChanged,
            now,
            user.Id,
            detailsJson: "{\"action\":\"password_reset_completed\"}");
        _dbContext.SecurityAuditEvents.Add(audit);

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<StaffMemberDto>> ListStaffAsync(
        TenantId? tenantId,
        Guid? branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var query = _dbContext.Memberships.AsQueryable();

        if (tenantId.HasValue)
        {
            query = query.Where(m => m.TenantId == tenantId.Value);
        }

        if (branchId.HasValue)
        {
            var bId = new BranchId(branchId.Value);
            query = query.Where(m => m.BranchId == bId);
        }

        var list = await query
            .Join(_dbContext.Users,
                m => m.UserId,
                u => u.Id,
                (m, u) => new StaffMemberDto(
                    u.Id.Value,
                    u.Email,
                    m.Role.ToString(),
                    m.BranchId.HasValue ? m.BranchId.Value.Value : (Guid?)null,
                    u.Status.ToString(),
                    u.CreatedAtUtc))
            .ToListAsync(ct);

        return list;
    }

    public async Task UpdateStaffStatusAsync(
        TenantId? tenantId,
        UpdateStaffStatusCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = new UserId(command.UserId);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
        {
            throw new InvalidOperationException("User not found.");
        }

        var effectiveTenantId = tenantId ?? TenantId.From(Guid.Empty);

        switch (command.Status)
        {
            case UserStatus.Active:
                user.Activate(now);
                break;
            case UserStatus.Suspended:
                user.Suspend(now);
                break;
            case UserStatus.Disabled:
                user.Disable(now);
                break;
            case UserStatus.Locked:
                user.Lock(now.AddMinutes(15), now);
                break;
        }

        var eventType = command.Status is UserStatus.Active
            ? SecurityAuditEventType.AccountUnlocked
            : SecurityAuditEventType.AccountLocked;

        var audit = SecurityAuditEvent.Create(
            effectiveTenantId,
            eventType,
            now,
            user.Id,
            detailsJson: $"{{\"newStatus\":\"{command.Status}\",\"updatedBy\":\"{actor.SubjectId}\"}}");
        _dbContext.SecurityAuditEvents.Add(audit);

        await _dbContext.SaveChangesAsync(ct);
    }

    private static void ValidateInviteAuthorization(
        AuthenticatedPrincipal actor,
        AuthRole targetRole,
        TenantId? tenantId,
        Guid? branchId)
    {
        switch (actor.Role)
        {
            case AuthRole.SuperAdmin:
                if (targetRole is not (AuthRole.SuperAdmin or AuthRole.RestaurantAdmin))
                {
                    throw new InvalidOperationException("SuperAdmin may only provision SuperAdmin or RestaurantAdmin roles.");
                }
                break;

            case AuthRole.RestaurantAdmin:
                if (targetRole is AuthRole.SuperAdmin or AuthRole.RestaurantAdmin)
                {
                    throw new InvalidOperationException("RestaurantAdmin cannot provision administrative roles at or above their tier.");
                }
                if (!tenantId.HasValue)
                {
                    throw new InvalidOperationException("Tenant required for RestaurantAdmin operations.");
                }
                break;

            case AuthRole.BranchManager:
                if (targetRole is AuthRole.SuperAdmin or AuthRole.RestaurantAdmin or AuthRole.BranchManager)
                {
                    throw new InvalidOperationException("BranchManager may only provision operational floor staff (Cashier, Kitchen, Bar, Waiter).");
                }
                if (!branchId.HasValue)
                {
                    throw new InvalidOperationException("Branch required for BranchManager operations.");
                }
                break;

            default:
                throw new InvalidOperationException("Actor role is not authorized to provision staff.");
        }
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
