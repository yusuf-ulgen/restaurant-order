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
public sealed partial class StaffIdentityService : IStaffIdentityService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IIamBootstrapGateway _bootstrapGateway;
    private readonly IIamUserLookupGateway _userLookupGateway;
    private readonly IPasswordHasher _passwordHasher;
    private static readonly TimeSpan DefaultInvitationTtl = TimeSpan.FromDays(2);
    private static readonly TimeSpan DefaultResetTtl = TimeSpan.FromHours(1);

    public StaffIdentityService(
        RestaurantOrderDbContext dbContext,
        IIamBootstrapGateway bootstrapGateway,
        IIamUserLookupGateway userLookupGateway,
        IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _bootstrapGateway = bootstrapGateway ?? throw new ArgumentNullException(nameof(bootstrapGateway));
        _userLookupGateway = userLookupGateway ?? throw new ArgumentNullException(nameof(userLookupGateway));
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

        var lookupUser = await _userLookupGateway.LookupUserForLoginAsync(normalizedEmail, ct);
        UserId userId;
        if (lookupUser == null)
        {
            var initialDummyHash = _passwordHasher.HashPassword(Guid.NewGuid().ToString("N")).Hash;
            var user = User.Create(command.Email, initialDummyHash, now, initialStatus: UserStatus.Pending);
            _dbContext.Users.Add(user);
            userId = user.Id;
        }
        else
        {
            userId = UserId.From(lookupUser.UserId);
        }

        if (command.Role == AuthRole.SuperAdmin)
        {
            throw new InvalidOperationException("SuperAdmin cannot be assigned as a tenant member via staff invitation. SuperAdmin accounts must be provisioned via platform bootstrap scripts. See docs/runbooks/database-migrations.md.");
        }

        if (!tenantId.HasValue || tenantId.Value.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Tenant required.");
        }

        var effectiveTenantId = tenantId.Value;
        var branchId = command.BranchId.HasValue ? new BranchId(command.BranchId.Value) : (BranchId?)null;

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(effectiveTenantId.Value, cancellationToken: ct);
        }

        try
        {
            var existingMembership = await _dbContext.Memberships
                .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == effectiveTenantId && m.BranchId == branchId && m.IsActive, ct);

            if (existingMembership != null)
            {
                throw new InvalidOperationException("User already has an active membership with this scope.");
            }

            var membership = UserMembership.Create(effectiveTenantId, userId, command.Role, branchId, now);
            _dbContext.Memberships.Add(membership);

            var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var tokenHash = ComputeSha256(rawToken);

            var invitation = InvitationToken.Create(effectiveTenantId, userId, tokenHash, DefaultInvitationTtl, now);
            _dbContext.InvitationTokens.Add(invitation);

            var audit = SecurityAuditEvent.Create(
                effectiveTenantId,
                SecurityAuditEventType.RoleAssigned,
                now,
                userId,
                branchId,
                detailsJson: $"{{\"role\":\"{command.Role}\",\"invitedBy\":\"{actor.SubjectId}\"}}");
            _dbContext.SecurityAuditEvents.Add(audit);

            await _dbContext.SaveChangesAsync(ct);
            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }

            return new InviteStaffResult(
                UserId: userId.Value,
                Email: command.Email,
                Role: command.Role,
                BranchId: command.BranchId,
                InvitationToken: rawToken,
                ExpiresAtUtc: invitation.ExpiresAtUtc);
        }
        finally
        {
            if (localTx != null)
            {
                await localTx.DisposeAsync();
            }
        }
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

        var invitationDto = await _bootstrapGateway.LookupInvitationTokenAsync(tokenHash, ct);
        if (invitationDto == null || invitationDto.IsConsumed || invitationDto.ExpiresAtUtc <= now)
        {
            throw new InvalidOperationException("Invalid, expired, or already consumed invitation token.");
        }

        var passwordHash = _passwordHasher.HashPassword(command.Password).Hash;

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx && invitationDto.TenantId != Guid.Empty)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(invitationDto.TenantId, cancellationToken: ct);
        }

        try
        {
            var invitation = await _dbContext.InvitationTokens
                .FirstOrDefaultAsync(t => t.Id == invitationDto.InvitationId, ct);
            if (invitation == null || !invitation.IsValid(now))
            {
                throw new InvalidOperationException("Invalid, expired, or already consumed invitation token.");
            }
            invitation.Consume(now);

            await _bootstrapGateway.ActivateUserAndSetPasswordAsync(invitationDto.UserId, passwordHash, now, ct);

            var audit = SecurityAuditEvent.Create(
                TenantId.From(invitationDto.TenantId),
                SecurityAuditEventType.PasswordChanged,
                now,
                UserId.From(invitationDto.UserId),
                detailsJson: "{\"action\":\"invitation_accepted\"}");
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

        var memberships = await query.ToListAsync(ct);
        var result = new List<StaffMemberDto>();

        foreach (var m in memberships)
        {
            var userSummary = await _bootstrapGateway.LookupUserByIdAsync(m.UserId.Value, ct);
            result.Add(new StaffMemberDto(
                m.UserId.Value,
                userSummary?.Email ?? "unknown",
                m.Role.ToString(),
                m.BranchId.HasValue ? m.BranchId.Value.Value : (Guid?)null,
                userSummary != null ? ((UserStatus)userSummary.Status).ToString() : "Unknown",
                DateTimeOffset.UtcNow));
        }

        return result;
    }

    public async Task UpdateStaffStatusAsync(
        TenantId? tenantId,
        UpdateStaffStatusCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var userSummary = await _bootstrapGateway.LookupUserByIdAsync(command.UserId, ct);
        if (userSummary == null)
        {
            throw new InvalidOperationException("User not found.");
        }

        if (!tenantId.HasValue || tenantId.Value.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Tenant required for staff status operations.");
        }

        var effectiveTenantId = tenantId.Value;
        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(effectiveTenantId.Value, cancellationToken: ct);
        }

        try
        {
            await _bootstrapGateway.UpdateUserStatusAsync(command.UserId, command.Status, now, ct);

            var eventType = command.Status is UserStatus.Active
                ? SecurityAuditEventType.AccountUnlocked
                : SecurityAuditEventType.AccountLocked;

            var audit = SecurityAuditEvent.Create(
                effectiveTenantId,
                eventType,
                now,
                UserId.From(command.UserId),
                detailsJson: $"{{\"newStatus\":\"{command.Status}\",\"updatedBy\":\"{actor.SubjectId}\"}}");
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

    private static void ValidateInviteAuthorization(
        AuthenticatedPrincipal actor,
        AuthRole targetRole,
        TenantId? tenantId,
        Guid? branchId)
    {
        if (targetRole == AuthRole.SuperAdmin)
        {
            throw new InvalidOperationException(
                "SuperAdmin cannot be assigned as a tenant member via staff invitation. SuperAdmin accounts must be provisioned via platform bootstrap scripts. See docs/runbooks/database-migrations.md.");
        }

        switch (actor.Role)
        {
            case AuthRole.SuperAdmin:
                if (targetRole is not AuthRole.RestaurantAdmin)
                {
                    throw new InvalidOperationException("SuperAdmin may only provision RestaurantAdmin roles via staff invitation.");
                }
                if (!tenantId.HasValue || tenantId.Value.Value == Guid.Empty)
                {
                    throw new InvalidOperationException("Tenant required for provisioning RestaurantAdmin.");
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
