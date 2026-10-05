using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Floor;

public partial class FloorService
{
    private void EnsureTenantAccess(TenantId tenantId, AuthenticatedPrincipal actor)
    {
        if (actor.Role == AuthRole.SuperAdmin)
        {
            throw new InvalidAuthorizationScopeException("SuperAdmin cannot manage branch resources directly without tenant context.");
        }

        if (!actor.Scope.TenantId.HasValue || actor.Scope.TenantId.Value != tenantId)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }
    }

    private void EnsureBranchAccess(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor)
    {
        EnsureTenantAccess(tenantId, actor);

        if (actor.Role == AuthRole.RestaurantAdmin)
        {
            return;
        }

        if (!actor.Scope.BranchId.HasValue || actor.Scope.BranchId.Value != branchId.Value)
        {
            throw new InvalidAuthorizationScopeException(
                $"{actor.Role} is not authorized to access branch '{branchId.Value}'.");
        }
    }

    private void EnsureTablesManagePermission(AuthenticatedPrincipal actor)
    {
        if (!_permissionRegistry.HasFullGrant(actor.Role, Permissions.BranchTablesManage))
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized to manage tables.");
        }
    }

    private void EnsureFloorStatusViewPermission(AuthenticatedPrincipal actor)
    {
        if (!_permissionRegistry.HasFullGrant(actor.Role, Permissions.FloorStatusView))
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized to view floor status.");
        }
    }

    private void EnsureSessionsManagePermission(AuthenticatedPrincipal actor)
    {
        if (!_permissionRegistry.HasFullGrant(actor.Role, Permissions.FloorSessionsManage))
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized to manage dining sessions.");
        }
    }

    private static void EnsureCustomerSessionAccess(DiningSession session, AuthenticatedPrincipal actor)
    {
        if (actor.Role == AuthRole.Customer)
        {
            if (!actor.TableSessionId.HasValue || actor.TableSessionId.Value != session.Id.Value)
            {
                throw new InvalidAuthorizationScopeException("Customer is not authorized to access foreign dining session.");
            }
        }
    }

    private async Task<Branch> GetBranchWithAccessCheckAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        return await _dbContext.Branches
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == branchId, ct)
            ?? throw new ResourceNotFoundException($"Branch '{branchId.Value}' was not found.");
    }

    private static void EnsureBranchAllowsFloorMutation(Branch branch)
    {
        if (branch.Status == BranchStatus.Closed)
        {
            throw new DomainException($"Cannot modify floor layout for closed branch '{branch.Id.Value}'.");
        }

        if (branch.Status == BranchStatus.Suspended)
        {
            throw new DomainException($"Cannot modify floor layout for suspended branch '{branch.Id.Value}'.");
        }
    }

    private static void VerifyConcurrencyToken(Guid expectedToken, Guid? providedToken)
    {
        if (!providedToken.HasValue || providedToken.Value == Guid.Empty)
        {
            throw new ConcurrencyPreconditionException("Concurrency token is required.");
        }

        if (providedToken.Value != expectedToken)
        {
            throw new ConcurrencyConflictException(
                $"Stale concurrency token. Provided '{providedToken.Value}', but current token is '{expectedToken}'.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private void AddAuditEvent(
        TenantId tenantId,
        string eventType,
        AuthenticatedPrincipal actor,
        BranchId? branchId,
        object details)
    {
        var auditEvent = SecurityAuditEvent.Create(
            tenantId: tenantId,
            eventType: eventType,
            nowUtc: DateTimeOffset.UtcNow,
            userId: actor.SubjectId != Guid.Empty ? new UserId(actor.SubjectId) : null,
            branchId: branchId,
            detailsJson: JsonSerializer.Serialize(details));

        _dbContext.SecurityAuditEvents.Add(auditEvent);
    }

    private async Task ExecuteInTenantTransactionAsync(
        TenantId tenantId,
        Func<Task> operation,
        CancellationToken ct)
    {
        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(tenantId.Value, cancellationToken: ct);
        }

        try
        {
            await operation();
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
