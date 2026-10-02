using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Auth;

public sealed partial class StaffIdentityService
{
    public async Task UpdateStaffStatusAsync(
        TenantId? tenantId,
        UpdateStaffStatusCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (!tenantId.HasValue || tenantId.Value.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Tenant required for staff status operations.");
        }

        if (actor.TenantId.HasValue && actor.TenantId.Value != tenantId.Value.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor does not have authority over this tenant.");
        }

        var effectiveTenantId = tenantId.Value;
        var targetUserId = UserId.From(command.UserId);

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(effectiveTenantId.Value, cancellationToken: ct);
        }

        try
        {
            var query = _dbContext.Memberships
                .Where(m => m.TenantId == effectiveTenantId && m.UserId == targetUserId);

            if (command.BranchId.HasValue)
            {
                var cmdBranch = new BranchId(command.BranchId.Value);
                query = query.Where(m => m.BranchId == cmdBranch);
            }

            var membership = await query.FirstOrDefaultAsync(ct);
            if (membership == null)
            {
                throw new InvalidOperationException("Staff membership not found.");
            }

            if (actor.Role == AuthRole.BranchManager)
            {
                if (!actor.BranchId.HasValue || membership.BranchId?.Value != actor.BranchId.Value)
                {
                    throw new InvalidAuthorizationScopeException("BranchManager may only update staff within their assigned branch.");
                }

                if (membership.Role is AuthRole.SuperAdmin or AuthRole.RestaurantAdmin or AuthRole.BranchManager)
                {
                    throw new InvalidAuthorizationScopeException("BranchManager cannot modify staff at or above their tier.");
                }
            }
            else if (actor.Role == AuthRole.RestaurantAdmin)
            {
                if (membership.Role is AuthRole.SuperAdmin or AuthRole.RestaurantAdmin)
                {
                    throw new InvalidAuthorizationScopeException("RestaurantAdmin cannot modify staff at or above their tier.");
                }
            }
            else if (actor.Role != AuthRole.SuperAdmin)
            {
                throw new InvalidAuthorizationScopeException("Actor role is not authorized to update staff status.");
            }

            // Update ONLY UserMembership.Status (isolated from global user status)
            membership.UpdateStatus(command.Status, now);

            // If suspended or disabled, immediately revoke ONLY sessions scoped to this tenant and branch
            if (command.Status is UserMembershipStatus.Suspended or UserMembershipStatus.Disabled)
            {
                await _sessionManager.RevokeMembershipSessionsAsync(
                    targetUserId,
                    effectiveTenantId.Value,
                    membership.BranchId?.Value,
                    ct);
            }

            var eventType = command.Status is UserMembershipStatus.Active
                ? SecurityAuditEventType.AccountUnlocked
                : SecurityAuditEventType.AccountLocked;

            var audit = SecurityAuditEvent.Create(
                effectiveTenantId,
                eventType,
                now,
                targetUserId,
                membership.BranchId,
                detailsJson: $"{{\"newMembershipStatus\":\"{command.Status}\",\"updatedBy\":\"{actor.SubjectId}\"}}");
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
