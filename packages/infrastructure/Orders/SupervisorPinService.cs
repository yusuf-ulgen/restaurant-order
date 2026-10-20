using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Orders;

/// <summary>
/// Authoritative atomic supervisor PIN verification service.
/// Enforces pessimistic FOR UPDATE locking on PinCredential in PostgreSQL,
/// commits attempt counters in an isolated transaction regardless of caller rollback,
/// strictly restricts supervisor override to active BranchManagers of the same branch,
/// and fails closed on persistence errors.
/// </summary>
public sealed class SupervisorPinService : ISupervisorPinService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPinHasher _pinHasher;
    private readonly IPermissionRegistry _permissionRegistry;

    public SupervisorPinService(
        IServiceScopeFactory scopeFactory,
        IPinHasher pinHasher,
        IPermissionRegistry permissionRegistry)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _pinHasher = pinHasher ?? throw new ArgumentNullException(nameof(pinHasher));
        _permissionRegistry = permissionRegistry ?? throw new ArgumentNullException(nameof(permissionRegistry));
    }

    public async Task<(Guid SupervisorId, string SupervisorRole)> VerifySupervisorPinAsync(
        TenantId tenantId,
        BranchId branchId,
        Guid? supervisorUserId,
        string? supervisorPin,
        CancellationToken cancellationToken)
    {
        if (!supervisorUserId.HasValue || string.IsNullOrWhiteSpace(supervisorPin))
        {
            throw new InvalidAuthorizationScopeException("Supervisor verification failed.");
        }

        var supervisorUser = new UserId(supervisorUserId.Value);
        var nowUtc = DateTimeOffset.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var secDb = scope.ServiceProvider.GetRequiredService<RestaurantOrderDbContext>();

        await using var secTx = await secDb.BeginTenantTransactionAsync(tenantId.Value, cancellationToken: cancellationToken);

        PinCredential? pinCred;
        if (secDb.Database.IsNpgsql())
        {
            pinCred = await secDb.PinCredentials
                .FromSqlInterpolated($"SELECT * FROM iam.pin_credentials WHERE tenant_id = {tenantId.Value} AND branch_id = {branchId.Value} AND user_id = {supervisorUser.Value} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            pinCred = await secDb.PinCredentials
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.BranchId == branchId && p.UserId == supervisorUser, cancellationToken);
        }

        if (pinCred == null || pinCred.IsLocked(nowUtc))
        {
            throw new InvalidAuthorizationScopeException("Supervisor verification failed.");
        }

        var verifyResult = _pinHasher.VerifyPin(supervisorPin.Trim(), pinCred.PinHash, pinCred.PepperKeyId);
        if (verifyResult == PinVerificationResult.Failed)
        {
            pinCred.RecordFailedAttempt(5, TimeSpan.FromMinutes(15), nowUtc);
            await secDb.SaveChangesAsync(cancellationToken);
            await secTx.CommitAsync(cancellationToken);
            throw new InvalidAuthorizationScopeException("Supervisor verification failed.");
        }

        // Supervisor MUST be an active BranchManager belonging to the exact same tenant and branch
        var membership = await secDb.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == supervisorUser, cancellationToken);

        if (membership == null || !membership.IsActive || membership.Status != UserMembershipStatus.Active)
        {
            throw new InvalidAuthorizationScopeException("Supervisor verification failed.");
        }

        if (membership.Role != AuthRole.BranchManager || membership.BranchId != branchId)
        {
            // RestaurantAdmin and other branch managers are strictly forbidden from supervisor PIN overrides
            throw new InvalidAuthorizationScopeException("Supervisor verification failed.");
        }

        var principalScope = AuthorizationScope.ForBranch(tenantId, branchId);
        var principal = AuthenticatedPrincipal.CreateStaff(
            supervisorUser.Value,
            AuthRole.BranchManager,
            principalScope,
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Pin,
            securityVersion: 1);

        if (!_permissionRegistry.IsPermitted(principal, Permissions.OrdersItemsVoidInPrep))
        {
            throw new InvalidAuthorizationScopeException("Supervisor verification failed.");
        }

        pinCred.RecordSuccessfulAttempt(nowUtc);
        await secDb.SaveChangesAsync(cancellationToken);
        await secTx.CommitAsync(cancellationToken);

        return (supervisorUser.Value, AuthRole.BranchManager.ToString());
    }
}
