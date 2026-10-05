using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Floor;

public partial class FloorService
{
    public async Task<BranchFloorStatusDto> GetBranchFloorStatusAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureFloorStatusViewPermission(actor);

        var tables = await _dbContext.RestaurantTables
            .Where(t => t.TenantId == tenantId && t.BranchId == branchId)
            .OrderBy(t => t.TableNumber)
            .ToListAsync(ct);

        var activeSessions = await _dbContext.DiningSessions
            .Where(s => s.TenantId == tenantId && s.BranchId == branchId && s.Status != DiningSessionStatus.Closed)
            .ToListAsync(ct);

        var sessionsByTable = activeSessions.ToDictionary(s => s.TableId, s => s);

        var tableStatusDtos = tables.Select(t => new TableFloorStatusDto(
            MapTable(t),
            sessionsByTable.TryGetValue(t.Id, out var session) ? MapSession(session) : null))
            .ToList();

        return new BranchFloorStatusDto(branchId.Value, tableStatusDtos);
    }

    public async Task<DiningSessionDto?> GetActiveSessionForTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var tableExists = await _dbContext.RestaurantTables
            .AnyAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct);

        if (!tableExists)
        {
            throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found in branch '{branchId.Value}'.");
        }

        var session = await _dbContext.DiningSessions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.TableId == tableId && s.Status != DiningSessionStatus.Closed, ct);

        if (session == null)
        {
            return null;
        }

        if (actor.Role == AuthRole.Customer)
        {
            EnsureCustomerSessionAccess(session, actor);
        }
        else
        {
            EnsureFloorStatusViewPermission(actor);
        }

        return MapSession(session);
    }

    public async Task<IReadOnlyList<DiningSessionDto>> ListSessionsForTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureFloorStatusViewPermission(actor);

        var tableExists = await _dbContext.RestaurantTables
            .AnyAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct);

        if (!tableExists)
        {
            throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found in branch '{branchId.Value}'.");
        }

        var sessions = await _dbContext.DiningSessions
            .Where(s => s.TenantId == tenantId && s.BranchId == branchId && s.TableId == tableId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);

        return sessions.Select(MapSession).ToList();
    }

    public async Task<DiningSessionDto> GetSessionAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var session = await _dbContext.DiningSessions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == sessionId, ct)
            ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found in branch '{branchId.Value}'.");

        if (actor.Role == AuthRole.Customer)
        {
            EnsureCustomerSessionAccess(session, actor);
        }
        else
        {
            EnsureFloorStatusViewPermission(actor);
        }

        return MapSession(session);
    }

    public async Task<DiningSessionDto> OpenSessionAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        OpenDiningSessionRequest request,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureSessionsManagePermission(actor);

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsFloorMutation(branch);

        var table = await _dbContext.RestaurantTables
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct)
            ?? throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found in branch '{branchId.Value}'.");

        if (!table.IsActive)
        {
            throw new DomainException("Cannot open dining session on an inactive table.");
        }

        var diningArea = await _dbContext.DiningAreas
            .FirstOrDefaultAsync(da => da.TenantId == tenantId && da.BranchId == branchId && da.Id == table.DiningAreaId, ct);

        if (diningArea == null || !diningArea.IsActive)
        {
            throw new DomainException("Cannot open dining session in an inactive DiningArea.");
        }

        var existingActive = await _dbContext.DiningSessions
            .AnyAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.TableId == tableId && s.Status != DiningSessionStatus.Closed, ct);

        if (existingActive)
        {
            throw new DuplicateCodeException("An open or active dining session already exists for this table.");
        }

        var session = DiningSession.Open(
            tenantId,
            branchId,
            table,
            request.GuestCount,
            request.AssignedWaiterId);

        _dbContext.DiningSessions.Add(session);

        try
        {
            await ExecuteInTenantTransactionAsync(tenantId, async () =>
            {
                AddAuditEvent(tenantId, SecurityAuditEventType.DiningSessionOpened, actor, branchId, new
                {
                    sessionId = session.Id.Value,
                    tableId = session.TableId.Value,
                    guestCount = session.GuestCount,
                    assignedWaiterId = session.AssignedWaiterId
                });

                await _dbContext.SaveChangesAsync(ct);
            }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateCodeException("An open or active dining session already exists for this table.");
        }

        return MapSession(session);
    }

    public async Task<DiningSessionDto> ActivateSessionAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureSessionsManagePermission(actor);

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsFloorMutation(branch);

        var session = await _dbContext.DiningSessions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == sessionId, ct)
            ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found in branch '{branchId.Value}'.");

        VerifyConcurrencyToken(session.ConcurrencyToken, concurrencyToken);

        session.Activate();

        await ExecuteInTenantTransactionAsync(tenantId, async () =>
        {
            AddAuditEvent(tenantId, SecurityAuditEventType.DiningSessionActivated, actor, branchId, new
            {
                sessionId = session.Id.Value,
                tableId = session.TableId.Value
            });

            await _dbContext.SaveChangesAsync(ct);
        }, ct);

        return MapSession(session);
    }

    public async Task<DiningSessionDto> RequestBillAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        if (actor.Role == AuthRole.Customer)
        {
            if (!actor.TableSessionId.HasValue || actor.TableSessionId.Value != sessionId.Value)
            {
                throw new InvalidAuthorizationScopeException("Customer is not authorized to access foreign dining session.");
            }
        }
        else
        {
            EnsureSessionsManagePermission(actor);
        }

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsFloorMutation(branch);

        var session = await _dbContext.DiningSessions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == sessionId, ct)
            ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found in branch '{branchId.Value}'.");

        if (actor.Role == AuthRole.Customer)
        {
            EnsureCustomerSessionAccess(session, actor);
        }

        VerifyConcurrencyToken(session.ConcurrencyToken, concurrencyToken);

        session.RequestBill();

        await ExecuteInTenantTransactionAsync(tenantId, async () =>
        {
            AddAuditEvent(tenantId, SecurityAuditEventType.DiningSessionBillRequested, actor, branchId, new
            {
                sessionId = session.Id.Value,
                tableId = session.TableId.Value
            });

            await _dbContext.SaveChangesAsync(ct);
        }, ct);

        return MapSession(session);
    }

    public async Task<DiningSessionDto> CloseSessionAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        CloseDiningSessionRequest request,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        if (actor.Role == AuthRole.Customer)
        {
            throw new InvalidAuthorizationScopeException("Customers are not authorized to close dining sessions.");
        }

        EnsureSessionsManagePermission(actor);

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsFloorMutation(branch);

        var session = await _dbContext.DiningSessions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == sessionId, ct)
            ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found in branch '{branchId.Value}'.");

        VerifyConcurrencyToken(session.ConcurrencyToken, concurrencyToken ?? request.ConcurrencyToken);

        session.Close(request.Reason);

        await ExecuteInTenantTransactionAsync(tenantId, async () =>
        {
            AddAuditEvent(tenantId, SecurityAuditEventType.DiningSessionClosed, actor, branchId, new
            {
                sessionId = session.Id.Value,
                tableId = session.TableId.Value,
                closeReason = session.CloseReason
            });

            await _dbContext.SaveChangesAsync(ct);
        }, ct);

        return MapSession(session);
    }

    private static DiningSessionDto MapSession(DiningSession session) => new(
        session.Id.Value,
        session.TenantId.Value,
        session.BranchId.Value,
        session.TableId.Value,
        session.Status.ToString(),
        session.GuestCount,
        session.AssignedWaiterId,
        session.OpenedAtUtc,
        session.ActivatedAtUtc,
        session.BillRequestedAtUtc,
        session.ClosedAtUtc,
        session.CloseReason,
        session.MergedIntoSessionId.HasValue ? session.MergedIntoSessionId.Value.Value : null,
        session.ConcurrencyToken,
        session.CreatedAtUtc,
        session.UpdatedAtUtc);
}
