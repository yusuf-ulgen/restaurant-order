using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    public async Task<DiningSessionDto> TransferSessionAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        TransferSessionRequest request,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureSessionsManagePermission(actor);

        if (actor.Role == AuthRole.Customer)
        {
            throw new InvalidAuthorizationScopeException("Customer is not authorized to transfer dining sessions.");
        }

        if (request.TargetTableId == Guid.Empty)
        {
            throw new DomainException("Target table ID cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.");
        }

        var idempotencyKey = request.IdempotencyKey.Trim();
        if (idempotencyKey.Length > 100)
        {
            throw new ArgumentException("Idempotency key cannot exceed 100 characters.");
        }

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsFloorMutation(branch);

        var targetTableId = new RestaurantTableId(request.TargetTableId);
        var requestFingerprint = DiningSessionMovement.ComputeFingerprint(
            tenantId,
            branchId,
            DiningSessionMovementType.Transfer,
            sessionId,
            targetTableId,
            null,
            request.Reason);
        DiningSessionDto? resultDto = null;

        try
        {
            await ExecuteInTenantTransactionAsync(tenantId, async () =>
            {
                // 1. Transaction-level advisory lock to serialize duplicate concurrent requests
                if (_dbContext.Database.IsNpgsql())
                {
                    var lockKey = $"idem:{tenantId.Value:N}:{branchId.Value:N}:{idempotencyKey}";
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", ct);
                }

                // 2. Idempotency check: replay previous transfer if key was already processed
                var existingMovement = await _dbContext.DiningSessionMovements
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.TenantId == tenantId &&
                                              m.BranchId == branchId &&
                                              m.IdempotencyKey == idempotencyKey, ct);

                if (existingMovement != null)
                {
                    if (!DiningSessionMovement.MatchesFingerprint(existingMovement.RequestFingerprint, requestFingerprint) ||
                        existingMovement.MovementType != DiningSessionMovementType.Transfer ||
                        existingMovement.SourceSessionId != sessionId ||
                        existingMovement.TargetTableId != targetTableId)
                    {
                        throw new ConcurrencyConflictException("Idempotency key was previously used for a different session movement or target.");
                    }

                    var currentSession = await _dbContext.DiningSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                                  s.BranchId == branchId &&
                                                  s.Id == sessionId, ct)
                        ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found.");

                    resultDto = MapSession(currentSession);
                    return;
                }

                // 3. Lock session row first
                if (_dbContext.Database.IsNpgsql())
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT id FROM tenancy.dining_sessions WHERE id = {sessionId.Value} AND tenant_id = {tenantId.Value} FOR UPDATE", ct);
                }

                // 4. Fetch source session after lock
                var session = await _dbContext.DiningSessions
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                              s.BranchId == branchId &&
                                              s.Id == sessionId, ct)
                    ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found in branch '{branchId.Value}'.");

                if (targetTableId == session.TableId)
                {
                    throw new DomainException("Cannot transfer session to the same table.");
                }

                // 5. Deterministic row-level locking on tables in ascending UUID order
                if (_dbContext.Database.IsNpgsql())
                {
                    var tableIdsToLock = new[] { session.TableId.Value, targetTableId.Value }
                        .Distinct()
                        .OrderBy(id => id)
                        .ToList();

                    foreach (var tid in tableIdsToLock)
                    {
                        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT id FROM tenancy.restaurant_tables WHERE id = {tid} AND tenant_id = {tenantId.Value} FOR UPDATE", ct);
                    }
                }

                // 6. Fetch target table after lock
                var targetTable = await _dbContext.RestaurantTables
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId &&
                                              t.BranchId == branchId &&
                                              t.Id == targetTableId, ct)
                    ?? throw new ResourceNotFoundException($"Target table '{targetTableId.Value}' was not found in branch '{branchId.Value}'.");

                // 7. Concurrency token validations
                VerifyConcurrencyToken(session.ConcurrencyToken, concurrencyToken ?? request.ConcurrencyToken);
                if (request.TargetTableConcurrencyToken.HasValue)
                {
                    VerifyConcurrencyToken(targetTable.ConcurrencyToken, request.TargetTableConcurrencyToken.Value);
                }

                // 8. Session status verification
                if (session.Status == DiningSessionStatus.Closed)
                {
                    throw new DomainException("Cannot transfer a closed dining session.");
                }

                if (session.Status == DiningSessionStatus.BillRequested)
                {
                    throw new DomainException(
                        "Cannot transfer dining session with bill requested. Bill requested sessions cannot be transferred.");
                }

                if (!targetTable.IsActive)
                {
                    throw new DomainException($"Cannot transfer to inactive table '{targetTable.TableNumber}'.");
                }

                if (session.GuestCount > targetTable.Capacity)
                {
                    throw new DomainException(
                        $"Guest count ({session.GuestCount}) exceeds target table capacity ({targetTable.Capacity}).");
                }

                // 9. Validate target table is not already occupied by another non-closed session
                var isTargetOccupied = await _dbContext.DiningSessions
                    .AnyAsync(s => s.TenantId == tenantId &&
                                   s.BranchId == branchId &&
                                   s.TableId == targetTableId &&
                                   s.Status != DiningSessionStatus.Closed &&
                                   s.Id != session.Id, ct);

                if (isTargetOccupied)
                {
                    throw new DomainException(
                        $"Target table '{targetTable.TableNumber}' already has an active dining session. Use merge endpoint instead of transfer.");
                }

                // 10. Execute transfer and record immutable movement
                var oldTableId = session.TableId;
                var nowUtc = DateTimeOffset.UtcNow;
                session.TransferToTable(targetTable);

                // Update operational table reference for all orders of this session so table filters & ready items resolve correctly
                var sessionOrders = await _dbContext.Orders
                    .Where(o => o.TenantId == tenantId && o.DiningSessionId == session.Id)
                    .ToListAsync(ct);
                foreach (var order in sessionOrders)
                {
                    order.UpdateOperationalTable(targetTableId, nowUtc);
                }

                // Update active customer service requests to point to the new table
                var activeRequests = await _dbContext.ServiceRequests
                    .Where(sr => sr.TenantId == tenantId && sr.DiningSessionId == session.Id &&
                                 sr.Status != ServiceRequestStatus.Completed && sr.Status != ServiceRequestStatus.Cancelled)
                    .ToListAsync(ct);
                foreach (var req in activeRequests)
                {
                    req.UpdateOperationalTable(targetTableId, nowUtc);
                }

                var movement = DiningSessionMovement.CreateTransfer(
                    tenantId,
                    branchId,
                    session.Id,
                    oldTableId,
                    targetTableId,
                    session.GuestCount,
                    actor.SubjectId,
                    actor.Role.ToString(),
                    idempotencyKey,
                    request.Reason,
                    requestFingerprint: requestFingerprint);

                _dbContext.DiningSessionMovements.Add(movement);

                AddAuditEvent(
                    tenantId,
                    "Floor.SessionTransferred",
                    actor,
                    branchId,
                    new
                    {
                        SessionId = session.Id.Value,
                        OldTableId = oldTableId.Value,
                        NewTableId = targetTableId.Value,
                        GuestCount = session.GuestCount,
                        IdempotencyKey = idempotencyKey
                    });

                _outboxWriter?.EnqueueEvent(
                    tenantId: tenantId.Value,
                    branchId: branchId.Value,
                    aggregateId: session.Id.Value.ToString(),
                    eventType: RestaurantOrder.Application.Events.RealtimeEventTypes.TableSessionTransferred,
                    data: new
                    {
                        SessionId = session.Id.Value,
                        OldTableId = oldTableId.Value,
                        NewTableId = targetTableId.Value,
                        TableNumber = targetTable.TableNumber
                    },
                    idempotencyKey: $"transfer-{idempotencyKey}",
                    occurredAtUtc: nowUtc);

                await _dbContext.SaveChangesAsync(ct);
                resultDto = MapSession(session);
            }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            var committed = await _dbContext.DiningSessionMovements
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.TenantId == tenantId &&
                                          m.BranchId == branchId &&
                                          m.IdempotencyKey == idempotencyKey, ct);
            if (committed != null)
            {
                if (!DiningSessionMovement.MatchesFingerprint(committed.RequestFingerprint, requestFingerprint) ||
                    committed.MovementType != DiningSessionMovementType.Transfer ||
                    committed.SourceSessionId != sessionId ||
                    committed.TargetTableId != targetTableId)
                {
                    throw new ConcurrencyConflictException("Idempotency key was previously used for a different session movement or target.");
                }

                var currentSession = await _dbContext.DiningSessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                              s.BranchId == branchId &&
                                              s.Id == sessionId, ct)
                    ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found.");

                resultDto = MapSession(currentSession);
            }
            else
            {
                throw;
            }
        }

        return resultDto!;
    }
}
