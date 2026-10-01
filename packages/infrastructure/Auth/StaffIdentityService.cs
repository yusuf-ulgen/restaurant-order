using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly IIdentityNotificationSender _notificationSender;
    private readonly IAuthSessionManager _sessionManager;
    private readonly ILogger<StaffIdentityService> _logger;
    private static readonly TimeSpan DefaultInvitationTtl = TimeSpan.FromDays(2);
    private static readonly TimeSpan DefaultResetTtl = TimeSpan.FromHours(1);

    public StaffIdentityService(
        RestaurantOrderDbContext dbContext,
        IIamBootstrapGateway bootstrapGateway,
        IIamUserLookupGateway userLookupGateway,
        IPasswordHasher passwordHasher,
        IIdentityNotificationSender notificationSender,
        IAuthSessionManager sessionManager,
        ILogger<StaffIdentityService>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _bootstrapGateway = bootstrapGateway ?? throw new ArgumentNullException(nameof(bootstrapGateway));
        _userLookupGateway = userLookupGateway ?? throw new ArgumentNullException(nameof(userLookupGateway));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _notificationSender = notificationSender ?? throw new ArgumentNullException(nameof(notificationSender));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? NullLogger<StaffIdentityService>.Instance;
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

            // Send notification within transaction so failure rolls back invitation token
            await _notificationSender.SendInvitationAsync(command.Email, rawToken, invitation.ExpiresAtUtc, ct);

            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }

            return new InviteStaffResult(
                UserId: userId.Value,
                Email: command.Email,
                Role: command.Role,
                BranchId: command.BranchId,
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

        // 1. Look up token metadata before beginning transaction to resolve tenant ID
        var tokenLookup = await _bootstrapGateway.LookupInvitationTokenAsync(tokenHash, ct);
        if (tokenLookup == null || tokenLookup.IsConsumed || tokenLookup.ExpiresAtUtc <= now)
        {
            throw new InvalidOperationException("Invalid, expired, or already consumed invitation token.");
        }

        var tenantId = tokenLookup.TenantId;
        var userId = tokenLookup.UserId;
        var passwordHash = _passwordHasher.HashPassword(command.Password).Hash;

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx && tenantId != Guid.Empty)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken: ct);
        }

        try
        {
            var dbConn = _dbContext.Database.GetDbConnection();
            var dbTx = _dbContext.Database.CurrentTransaction!.GetDbTransaction();

            // 2. Consume token atomically inside transaction
            var consumed = await _bootstrapGateway.ConsumeInvitationTokenInTransactionAsync(
                dbConn,
                dbTx,
                tokenHash,
                now,
                ct);

            if (consumed == null)
            {
                throw new InvalidOperationException("Invalid, expired, or already consumed invitation token.");
            }

            // 3. Activate user & set password inside transaction
            await _bootstrapGateway.ActivateUserAndSetPasswordInTransactionAsync(
                dbConn,
                dbTx,
                userId,
                passwordHash,
                now,
                ct);

            // 4. Update UserMembership to Active in DbContext if found
            var membership = await _dbContext.Memberships
                .FirstOrDefaultAsync(m => m.TenantId == TenantId.From(tenantId) && m.UserId == UserId.From(userId), ct);
            if (membership != null && membership.Status != UserMembershipStatus.Active)
            {
                membership.UpdateStatus(UserMembershipStatus.Active, now);
            }

            // 5. Security audit event
            var audit = SecurityAuditEvent.Create(
                TenantId.From(tenantId),
                SecurityAuditEventType.PasswordChanged,
                now,
                UserId.From(userId),
                detailsJson: "{\"action\":\"invitation_accepted\"}");
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
    }

    public async Task<IReadOnlyList<StaffMemberDto>> ListStaffAsync(
        TenantId? tenantId,
        Guid? branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        if (!tenantId.HasValue || tenantId.Value.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Tenant required.");
        }

        if (actor.TenantId.HasValue && actor.TenantId.Value != tenantId.Value.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor does not have authority over this tenant.");
        }

        if (actor.Role == AuthRole.BranchManager)
        {
            if (!actor.BranchId.HasValue)
            {
                throw new InvalidAuthorizationScopeException("BranchManager requires an assigned branch.");
            }

            if (branchId.HasValue && branchId.Value != actor.BranchId.Value)
            {
                throw new InvalidAuthorizationScopeException("BranchManager cannot list staff from another branch.");
            }

            branchId = actor.BranchId.Value;
        }
        else if (actor.Role != AuthRole.SuperAdmin && actor.Role != AuthRole.RestaurantAdmin)
        {
            throw new InvalidAuthorizationScopeException("Actor role is not authorized to list staff.");
        }

        var query = _dbContext.Memberships.AsQueryable()
            .Where(m => m.TenantId == tenantId.Value);

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
                m.Status.ToString(),
                m.CreatedAtUtc));
        }

        return result;
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
                    throw new InvalidAuthorizationScopeException("SuperAdmin may only provision RestaurantAdmin roles via staff invitation.");
                }
                if (!tenantId.HasValue || tenantId.Value.Value == Guid.Empty)
                {
                    throw new InvalidOperationException("Tenant required for provisioning RestaurantAdmin.");
                }
                break;

            case AuthRole.RestaurantAdmin:
                if (actor.TenantId.HasValue && (!tenantId.HasValue || actor.TenantId.Value != tenantId.Value.Value))
                {
                    throw new InvalidAuthorizationScopeException("Actor does not have authority over this tenant.");
                }
                if (targetRole is AuthRole.SuperAdmin or AuthRole.RestaurantAdmin)
                {
                    throw new InvalidAuthorizationScopeException("RestaurantAdmin cannot provision administrative roles at or above their tier.");
                }
                if (!tenantId.HasValue)
                {
                    throw new InvalidOperationException("Tenant required for RestaurantAdmin operations.");
                }
                break;

            case AuthRole.BranchManager:
                if (actor.TenantId.HasValue && (!tenantId.HasValue || actor.TenantId.Value != tenantId.Value.Value))
                {
                    throw new InvalidAuthorizationScopeException("Actor does not have authority over this tenant.");
                }
                if (!actor.BranchId.HasValue || !branchId.HasValue || actor.BranchId.Value != branchId.Value)
                {
                    throw new InvalidAuthorizationScopeException("BranchManager cannot invite staff outside their assigned branch.");
                }
                if (targetRole is AuthRole.SuperAdmin or AuthRole.RestaurantAdmin or AuthRole.BranchManager)
                {
                    throw new InvalidAuthorizationScopeException("BranchManager may only provision operational floor staff (Cashier, Kitchen, Bar, Waiter).");
                }
                break;

            default:
                throw new InvalidAuthorizationScopeException("Actor role is not authorized to provision staff.");
        }
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
