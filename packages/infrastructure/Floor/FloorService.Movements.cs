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

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey.Trim();

        DiningSessionDto? resultDto = null;

        await ExecuteInTenantTransactionAsync(tenantId, async () =>
        {
            // 1. Idempotency check: replay previous transfer if key was already processed
            var existingMovement = await _dbContext.DiningSessionMovements
                .FirstOrDefaultAsync(m => m.TenantId == tenantId &&
                                          m.BranchId == branchId &&
                                          m.IdempotencyKey == idempotencyKey, ct);

            if (existingMovement != null)
            {
                if (existingMovement.MovementType != DiningSessionMovementType.Transfer ||
                    existingMovement.SourceSessionId != sessionId)
                {
                    throw new DomainException("Idempotency key was previously used for a different session movement.");
                }

                var currentSession = await _dbContext.DiningSessions
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                              s.BranchId == branchId &&
                                              s.Id == sessionId, ct)
                    ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found.");

                resultDto = MapSession(currentSession);
                return;
            }

            // 2. Fetch source session
            var session = await _dbContext.DiningSessions
                .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                          s.BranchId == branchId &&
                                          s.Id == sessionId, ct)
                ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found in branch '{branchId.Value}'.");

            // 3. Concurrency token validation
            VerifyConcurrencyToken(session.ConcurrencyToken, concurrencyToken ?? request.ConcurrencyToken);

            // 4. Session status verification (safe default: only Open or Active can transfer)
            if (session.Status == DiningSessionStatus.Closed)
            {
                throw new DomainException("Cannot transfer a closed dining session.");
            }

            if (session.Status == DiningSessionStatus.BillRequested)
            {
                throw new DomainException(
                    "Cannot transfer dining session with bill requested. Bill requested sessions cannot be transferred.");
            }

            var targetTableId = new RestaurantTableId(request.TargetTableId);
            if (targetTableId == session.TableId)
            {
                throw new DomainException("Cannot transfer session to the same table.");
            }

            // 5. Deterministic row-level locking on tables and session in ascending UUID order
            if (_dbContext.Database.IsNpgsql())
            {
                var tableIdsToLock = new[] { session.TableId.Value, targetTableId.Value }
                    .OrderBy(id => id)
                    .ToList();

                foreach (var tid in tableIdsToLock)
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT id FROM tenancy.restaurant_tables WHERE id = {tid} AND tenant_id = {tenantId.Value} FOR UPDATE", ct);
                }

                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM tenancy.dining_sessions WHERE id = {session.Id.Value} AND tenant_id = {tenantId.Value} FOR UPDATE", ct);
            }

            // 6. Validate target table existence and status
            var targetTable = await _dbContext.RestaurantTables
                .FirstOrDefaultAsync(t => t.TenantId == tenantId &&
                                          t.BranchId == branchId &&
                                          t.Id == targetTableId, ct)
                ?? throw new ResourceNotFoundException($"Target table '{targetTableId.Value}' was not found in branch '{branchId.Value}'.");

            if (!targetTable.IsActive)
            {
                throw new DomainException($"Cannot transfer to inactive table '{targetTable.TableNumber}'.");
            }

            if (session.GuestCount > targetTable.Capacity)
            {
                throw new DomainException(
                    $"Guest count ({session.GuestCount}) exceeds target table capacity ({targetTable.Capacity}).");
            }

            // 7. Validate target table is not already occupied by another non-closed session
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

            // 8. Execute transfer and record immutable movement
            var oldTableId = session.TableId;
            session.TransferToTable(targetTable);

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
                request.Reason);

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

            await _dbContext.SaveChangesAsync(ct);
            resultDto = MapSession(session);
        }, ct);

        return resultDto!;
    }

    public async Task<DiningSessionDto> MergeSessionAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sourceSessionId,
        MergeSessionRequest request,
        Guid? sourceConcurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureSessionsManagePermission(actor);

        if (actor.Role == AuthRole.Customer)
        {
            throw new InvalidAuthorizationScopeException("Customer is not authorized to merge dining sessions.");
        }

        if (request.TargetSessionId == Guid.Empty)
        {
            throw new DomainException("Target session ID cannot be empty.");
        }

        if (sourceSessionId.Value == request.TargetSessionId)
        {
            throw new DomainException("Cannot merge a dining session into itself.");
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey.Trim();

        var targetSessionId = new DiningSessionId(request.TargetSessionId);
        DiningSessionDto? resultDto = null;

        await ExecuteInTenantTransactionAsync(tenantId, async () =>
        {
            // 1. Idempotency check: replay previous merge if key was already processed
            var existingMovement = await _dbContext.DiningSessionMovements
                .FirstOrDefaultAsync(m => m.TenantId == tenantId &&
                                          m.BranchId == branchId &&
                                          m.IdempotencyKey == idempotencyKey, ct);

            if (existingMovement != null)
            {
                if (existingMovement.MovementType != DiningSessionMovementType.Merge ||
                    existingMovement.SourceSessionId != sourceSessionId)
                {
                    throw new DomainException("Idempotency key was previously used for a different session movement.");
                }

                var currentTarget = await _dbContext.DiningSessions
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                              s.BranchId == branchId &&
                                              s.Id == targetSessionId, ct)
                    ?? throw new ResourceNotFoundException($"Target dining session '{targetSessionId.Value}' was not found.");

                resultDto = MapSession(currentTarget);
                return;
            }

            // 2. Fetch source and target sessions
            var sourceSession = await _dbContext.DiningSessions
                .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                          s.BranchId == branchId &&
                                          s.Id == sourceSessionId, ct)
                ?? throw new ResourceNotFoundException($"Source dining session '{sourceSessionId.Value}' was not found in branch '{branchId.Value}'.");

            var targetSession = await _dbContext.DiningSessions
                .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                          s.BranchId == branchId &&
                                          s.Id == targetSessionId, ct)
                ?? throw new ResourceNotFoundException($"Target dining session '{targetSessionId.Value}' was not found in branch '{branchId.Value}'.");

            // 3. Concurrency token validations
            VerifyConcurrencyToken(sourceSession.ConcurrencyToken, sourceConcurrencyToken ?? request.SourceConcurrencyToken);
            if (request.TargetConcurrencyToken.HasValue)
            {
                VerifyConcurrencyToken(targetSession.ConcurrencyToken, request.TargetConcurrencyToken.Value);
            }

            // 4. Validate session statuses
            if (sourceSession.Status == DiningSessionStatus.Closed)
            {
                throw new DomainException("Source dining session is already closed.");
            }

            if (sourceSession.Status == DiningSessionStatus.BillRequested)
            {
                throw new DomainException("Cannot merge a dining session with bill requested.");
            }

            if (targetSession.Status == DiningSessionStatus.Closed)
            {
                throw new DomainException("Target dining session is already closed.");
            }

            if (targetSession.Status == DiningSessionStatus.BillRequested)
            {
                throw new DomainException("Cannot merge into a target dining session with bill requested.");
            }

            // 5. Deterministic row-level locking on tables and sessions in ascending UUID order
            if (_dbContext.Database.IsNpgsql())
            {
                var tableIdsToLock = new[] { sourceSession.TableId.Value, targetSession.TableId.Value }
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();

                foreach (var tid in tableIdsToLock)
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT id FROM tenancy.restaurant_tables WHERE id = {tid} AND tenant_id = {tenantId.Value} FOR UPDATE", ct);
                }

                var sessionIdsToLock = new[] { sourceSession.Id.Value, targetSession.Id.Value }
                    .OrderBy(id => id)
                    .ToList();

                foreach (var sid in sessionIdsToLock)
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT id FROM tenancy.dining_sessions WHERE id = {sid} AND tenant_id = {tenantId.Value} FOR UPDATE", ct);
                }
            }

            // 6. Validate target table capacity for combined guest count
            var targetTable = await _dbContext.RestaurantTables
                .FirstOrDefaultAsync(t => t.TenantId == tenantId &&
                                          t.BranchId == branchId &&
                                          t.Id == targetSession.TableId, ct)
                ?? throw new ResourceNotFoundException($"Target table was not found for session '{targetSession.Id.Value}'.");

            if (!targetTable.IsActive)
            {
                throw new DomainException($"Cannot merge into session at inactive table '{targetTable.TableNumber}'.");
            }

            var combinedGuests = targetSession.GuestCount + sourceSession.GuestCount;
            if (combinedGuests > targetTable.Capacity)
            {
                throw new DomainException(
                    $"Combined guest count ({combinedGuests}) exceeds target table capacity ({targetTable.Capacity}).");
            }

            // 7. Execute merge domain operations
            var guestsMoved = sourceSession.GuestCount;
            targetSession.AcceptMergedGuests(guestsMoved, targetTable);
            sourceSession.MergeInto(targetSession, request.Reason);

            var movement = DiningSessionMovement.CreateMerge(
                tenantId,
                branchId,
                sourceSession.Id,
                sourceSession.TableId,
                targetSession.Id,
                targetSession.TableId,
                guestsMoved,
                actor.SubjectId,
                actor.Role.ToString(),
                idempotencyKey,
                request.Reason);

            _dbContext.DiningSessionMovements.Add(movement);

            AddAuditEvent(
                tenantId,
                "Floor.SessionMerged",
                actor,
                branchId,
                new
                {
                    SourceSessionId = sourceSession.Id.Value,
                    TargetSessionId = targetSession.Id.Value,
                    GuestsMoved = guestsMoved,
                    CombinedGuests = targetSession.GuestCount,
                    IdempotencyKey = idempotencyKey
                });

            await _dbContext.SaveChangesAsync(ct);

            // 8. Invalidate source customer session cache immediately
            if (_customerSessionValidator != null)
            {
                await _customerSessionValidator.InvalidateSessionCacheAsync(
                    tenantId.Value, branchId.Value, sourceSession.Id.Value, ct);
            }

            resultDto = MapSession(targetSession);
        }, ct);

        return resultDto!;
    }
}
