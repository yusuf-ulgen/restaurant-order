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

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.");
        }

        var idempotencyKey = request.IdempotencyKey.Trim();
        if (idempotencyKey.Length > 100)
        {
            throw new ArgumentException("Idempotency key cannot exceed 100 characters.");
        }

        if (!sourceConcurrencyToken.HasValue)
        {
            throw new ArgumentException("Source concurrency token is required.");
        }

        if (!request.TargetConcurrencyToken.HasValue)
        {
            throw new ArgumentException("Target concurrency token is required.");
        }

        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        EnsureBranchAllowsFloorMutation(branch);

        var targetSessionId = new DiningSessionId(request.TargetSessionId);
        var requestFingerprint = DiningSessionMovement.ComputeFingerprint(
            tenantId,
            branchId,
            DiningSessionMovementType.Merge,
            sourceSessionId,
            null,
            targetSessionId,
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

                // 2. Idempotency check: replay previous merge if key was already processed
                var existingMovement = await _dbContext.DiningSessionMovements
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.TenantId == tenantId &&
                                              m.BranchId == branchId &&
                                              m.IdempotencyKey == idempotencyKey, ct);

                if (existingMovement != null)
                {
                    if (!DiningSessionMovement.MatchesFingerprint(existingMovement.RequestFingerprint, requestFingerprint) ||
                        existingMovement.MovementType != DiningSessionMovementType.Merge ||
                        existingMovement.SourceSessionId != sourceSessionId ||
                        existingMovement.TargetSessionId != targetSessionId)
                    {
                        throw new ConcurrencyConflictException("Idempotency key was previously used for a different session movement or target.");
                    }

                    var currentTarget = await _dbContext.DiningSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                                  s.BranchId == branchId &&
                                                  s.Id == targetSessionId, ct)
                        ?? throw new ResourceNotFoundException($"Target dining session '{targetSessionId.Value}' was not found.");

                    resultDto = MapSession(currentTarget);
                    return;
                }

                // 3. Acquire row locks deterministically ordered by Id (ascending) to prevent deadlocks
                var firstLockId = sourceSessionId.Value.CompareTo(targetSessionId.Value) < 0 ? sourceSessionId.Value : targetSessionId.Value;
                var secondLockId = sourceSessionId.Value.CompareTo(targetSessionId.Value) < 0 ? targetSessionId.Value : sourceSessionId.Value;

                if (_dbContext.Database.IsNpgsql())
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT id FROM tenancy.dining_sessions WHERE id = {firstLockId} AND tenant_id = {tenantId.Value} FOR UPDATE", ct);
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT id FROM tenancy.dining_sessions WHERE id = {secondLockId} AND tenant_id = {tenantId.Value} FOR UPDATE", ct);
                }

                // 4. Fetch entities fresh from DB after lock acquisition
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

                // 5. Concurrency token validations
                if (sourceSession.ConcurrencyToken != sourceConcurrencyToken.Value)
                {
                    throw new ConcurrencyConflictException("Source dining session was modified by another operation.");
                }

                if (targetSession.ConcurrencyToken != request.TargetConcurrencyToken.Value)
                {
                    throw new ConcurrencyConflictException("Target dining session was modified by another operation.");
                }

                // 6. Business state assertions
                if (sourceSession.Status == DiningSessionStatus.Closed)
                {
                    throw new DomainException("Source dining session is already closed.");
                }

                if (sourceSession.Status == DiningSessionStatus.BillRequested)
                {
                    throw new DomainException("Cannot merge a dining session for which bill has already been requested.");
                }

                if (targetSession.Status == DiningSessionStatus.Closed)
                {
                    throw new DomainException("Target dining session is closed.");
                }

                if (targetSession.Status == DiningSessionStatus.BillRequested)
                {
                    throw new DomainException("Cannot merge into a target dining session for which bill has already been requested.");
                }

                // 7. Lock tables in ascending Id order
                var firstTableLockId = sourceSession.TableId.Value.CompareTo(targetSession.TableId.Value) < 0
                    ? sourceSession.TableId.Value
                    : targetSession.TableId.Value;
                var secondTableLockId = sourceSession.TableId.Value.CompareTo(targetSession.TableId.Value) < 0
                    ? targetSession.TableId.Value
                    : sourceSession.TableId.Value;

                if (_dbContext.Database.IsNpgsql())
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT id FROM tenancy.restaurant_tables WHERE id = {firstTableLockId} AND tenant_id = {tenantId.Value} FOR UPDATE", ct);

                    if (firstTableLockId != secondTableLockId)
                    {
                        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT id FROM tenancy.restaurant_tables WHERE id = {secondTableLockId} AND tenant_id = {tenantId.Value} FOR UPDATE", ct);
                    }
                }

                var sourceTable = await _dbContext.RestaurantTables
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId &&
                                              t.BranchId == branchId &&
                                              t.Id == sourceSession.TableId, ct)
                    ?? throw new ResourceNotFoundException($"Source table '{sourceSession.TableId.Value}' was not found.");

                var targetTable = await _dbContext.RestaurantTables
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId &&
                                              t.BranchId == branchId &&
                                              t.Id == targetSession.TableId, ct)
                    ?? throw new ResourceNotFoundException($"Target table '{targetSession.TableId.Value}' was not found.");

                var guestsToMove = sourceSession.GuestCount;
                var now = DateTimeOffset.UtcNow;

                // 8. Domain mutations
                targetSession.AcceptMergedGuests(guestsToMove, targetTable);
                sourceSession.MergeInto(targetSession, request.Reason);

                // 9. Create movement audit log
                var movement = DiningSessionMovement.CreateMerge(
                    tenantId,
                    branchId,
                    sourceSessionId,
                    sourceSession.TableId,
                    targetSessionId,
                    targetSession.TableId,
                    guestsToMove,
                    actor.SubjectId,
                    actor.Role.ToString(),
                    idempotencyKey,
                    request.Reason,
                    now,
                    requestFingerprint: requestFingerprint);

                _dbContext.DiningSessionMovements.Add(movement);

                AddAuditEvent(
                    tenantId,
                    "Floor.SessionMerged",
                    actor,
                    branchId,
                    new
                    {
                        SourceSessionId = sourceSessionId.Value,
                        TargetSessionId = targetSessionId.Value,
                        GuestsMoved = guestsToMove,
                        IdempotencyKey = idempotencyKey
                    });

                await _dbContext.SaveChangesAsync(ct);
                resultDto = MapSession(targetSession);
            }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Unique constraint race condition handled gracefully
            var existingMovement = await _dbContext.DiningSessionMovements
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.TenantId == tenantId &&
                                          m.BranchId == branchId &&
                                          m.IdempotencyKey == idempotencyKey, ct);

            if (existingMovement != null)
            {
                if (!DiningSessionMovement.MatchesFingerprint(existingMovement.RequestFingerprint, requestFingerprint) ||
                    existingMovement.MovementType != DiningSessionMovementType.Merge ||
                    existingMovement.SourceSessionId != sourceSessionId ||
                    existingMovement.TargetSessionId != targetSessionId)
                {
                    throw new ConcurrencyConflictException("Idempotency key was previously used for a different session movement or target.");
                }

                var currentTarget = await _dbContext.DiningSessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                              s.BranchId == branchId &&
                                              s.Id == targetSessionId, ct)
                    ?? throw new ResourceNotFoundException($"Target dining session '{targetSessionId.Value}' was not found.");

                resultDto = MapSession(currentTarget);
            }
            else
            {
                throw;
            }
        }

        // Invalidate source customer session cache AFTER successful commit
        if (_customerSessionValidator != null)
        {
            try
            {
                await _customerSessionValidator.InvalidateSessionCacheAsync(
                    tenantId.Value, branchId.Value, sourceSessionId.Value, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to invalidate customer session cache for merged session {SessionId}. Retrying...", sourceSessionId.Value);
                try
                {
                    await _customerSessionValidator.InvalidateSessionCacheAsync(
                        tenantId.Value, branchId.Value, sourceSessionId.Value, ct);
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "Retry failed to invalidate customer session cache for merged session {SessionId}.", sourceSessionId.Value);
                }
            }
        }

        return resultDto!;
    }
}
