using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Floor;

/// <summary>
/// Infrastructure implementation of staff service request operations.
/// Strictly enforces fail-closed multi-tenant RBAC (service.request.manage),
/// atomic optimistic concurrency via ConcurrencyToken, and transactional outbox event emission.
/// </summary>
public sealed class StaffServiceRequestService : IStaffServiceRequestService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IPermissionRegistry _permissionRegistry;
    private readonly IOperationalOutboxWriter? _outboxWriter;
    private readonly ILogger<StaffServiceRequestService>? _logger;

    public StaffServiceRequestService(
        RestaurantOrderDbContext dbContext,
        IPermissionRegistry permissionRegistry,
        IOperationalOutboxWriter? outboxWriter = null,
        ILogger<StaffServiceRequestService>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _permissionRegistry = permissionRegistry ?? throw new ArgumentNullException(nameof(permissionRegistry));
        _outboxWriter = outboxWriter;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StaffServiceRequestDto>> ListServiceRequestsAsync(
        TenantId tenantId,
        BranchId branchId,
        ServiceRequestStatus? statusFilter,
        RestaurantTableId? tableFilter,
        DiningSessionId? sessionFilter,
        int limit,
        int offset,
        AuthenticatedPrincipal actor,
        bool activeOnly = false,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureServiceRequestManagePermission(actor);

        var safeLimit = Math.Clamp(limit, 1, 200);
        var safeOffset = Math.Max(0, offset);

        var query = from sr in _dbContext.ServiceRequests.AsNoTracking()
                    where sr.TenantId == tenantId && sr.BranchId == branchId
                    join t in _dbContext.RestaurantTables.AsNoTracking()
                        on new { sr.TenantId, sr.BranchId, sr.TableId }
                        equals new { t.TenantId, t.BranchId, TableId = t.Id }
                    select new
                    {
                        sr.Id,
                        sr.TenantId,
                        sr.BranchId,
                        sr.DiningSessionId,
                        sr.TableId,
                        t.TableNumber,
                        RequestType = sr.RequestType.ToString(),
                        Status = sr.Status,
                        sr.Note,
                        sr.CreatedAtUtc,
                        sr.AcknowledgedAtUtc,
                        sr.CompletedAtUtc,
                        sr.ConcurrencyToken
                    };

        if (activeOnly)
        {
            query = query.Where(x => x.Status == ServiceRequestStatus.Open || x.Status == ServiceRequestStatus.Acknowledged);
        }
        else if (statusFilter.HasValue)
        {
            var st = statusFilter.Value;
            query = query.Where(x => x.Status == st);
        }

        if (tableFilter != null)
        {
            var tid = tableFilter;
            query = query.Where(x => x.TableId == tid);
        }

        if (sessionFilter != null)
        {
            var sid = sessionFilter;
            query = query.Where(x => x.DiningSessionId == sid);
        }

        var results = await query
            .OrderBy(x => x.Status == ServiceRequestStatus.Open ? 0 :
                          x.Status == ServiceRequestStatus.Acknowledged ? 1 : 2)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Skip(safeOffset)
            .Take(safeLimit)
            .ToListAsync(ct);

        return results.Select(r => new StaffServiceRequestDto(
            r.Id.Value,
            r.TenantId.Value,
            r.BranchId.Value,
            r.DiningSessionId.Value,
            r.TableId.Value,
            r.TableNumber,
            r.RequestType,
            r.Status.ToString(),
            r.Note,
            r.CreatedAtUtc,
            r.AcknowledgedAtUtc,
            r.CompletedAtUtc,
            r.ConcurrencyToken
        )).ToList();
    }

    public async Task<StaffServiceRequestDto> AcknowledgeRequestAsync(
        TenantId tenantId,
        BranchId branchId,
        ServiceRequestId requestId,
        Guid concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureServiceRequestManagePermission(actor);

        var localTx = _dbContext.Database.CurrentTransaction == null
            ? await _dbContext.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            var sr = await _dbContext.ServiceRequests
                .FirstOrDefaultAsync(x => x.Id == requestId &&
                                          x.TenantId == tenantId &&
                                          x.BranchId == branchId, ct)
                ?? throw new ResourceNotFoundException($"Service request '{requestId.Value}' was not found.");

            if (sr.ConcurrencyToken != concurrencyToken)
            {
                throw new ConcurrencyConflictException("Service request concurrency token mismatch. It may have been updated or cancelled concurrently.");
            }

            if (sr.Status == ServiceRequestStatus.Cancelled)
            {
                throw new ConcurrencyConflictException("Cannot acknowledge service request: it has already been cancelled by the customer.");
            }

            if (sr.Status != ServiceRequestStatus.Open)
            {
                throw new ConcurrencyConflictException($"Cannot acknowledge service request in status '{sr.Status}'. Only Open requests can be acknowledged.");
            }

            sr.Acknowledge();

            var nowUtc = DateTimeOffset.UtcNow;
            _outboxWriter?.EnqueueEvent(
                tenantId: tenantId.Value,
                branchId: branchId.Value,
                aggregateId: sr.Id.Value.ToString(),
                eventType: RealtimeEventTypes.TableServiceRequestStatusChanged,
                data: new TableServiceRequestStatusChangedEventData(
                    sr.Id.Value,
                    sr.TableId.Value,
                    sr.DiningSessionId.Value,
                    ServiceRequestStatus.Acknowledged.ToString(),
                    nowUtc),
                idempotencyKey: $"sr-ack-{sr.Id.Value}-{sr.ConcurrencyToken:D}",
                occurredAtUtc: nowUtc);

            var audit = SecurityAuditEvent.Create(
                tenantId,
                SecurityAuditEventType.StaffServiceRequestAcknowledged,
                nowUtc,
                userId: new UserId(actor.SubjectId),
                branchId: branchId,
                detailsJson: JsonSerializer.Serialize(new
                {
                    requestId = sr.Id.Value,
                    sessionId = sr.DiningSessionId.Value,
                    tableId = sr.TableId.Value,
                    requestType = sr.RequestType.ToString(),
                    actorId = actor.SubjectId,
                    actorRole = actor.Role.ToString()
                }));
            _dbContext.SecurityAuditEvents.Add(audit);

            try
            {
                await _dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ConcurrencyConflictException("Service request was modified concurrently by another transaction.", ex);
            }

            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }

            var tableNumber = await _dbContext.RestaurantTables
                .Where(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == sr.TableId)
                .Select(t => t.TableNumber)
                .FirstOrDefaultAsync(ct);

            return MapToStaffDto(sr, tableNumber ?? string.Empty);
        }
        finally
        {
            if (localTx != null)
            {
                await localTx.DisposeAsync();
            }
        }
    }

    public async Task<StaffServiceRequestDto> CompleteRequestAsync(
        TenantId tenantId,
        BranchId branchId,
        ServiceRequestId requestId,
        Guid concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureServiceRequestManagePermission(actor);

        var localTx = _dbContext.Database.CurrentTransaction == null
            ? await _dbContext.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            var sr = await _dbContext.ServiceRequests
                .FirstOrDefaultAsync(x => x.Id == requestId &&
                                          x.TenantId == tenantId &&
                                          x.BranchId == branchId, ct)
                ?? throw new ResourceNotFoundException($"Service request '{requestId.Value}' was not found.");

            if (sr.ConcurrencyToken != concurrencyToken)
            {
                throw new ConcurrencyConflictException("Service request concurrency token mismatch. It may have been updated or cancelled concurrently.");
            }

            if (sr.Status == ServiceRequestStatus.Cancelled)
            {
                throw new ConcurrencyConflictException("Cannot complete service request: it has already been cancelled by the customer.");
            }

            if (sr.Status != ServiceRequestStatus.Open && sr.Status != ServiceRequestStatus.Acknowledged)
            {
                throw new ConcurrencyConflictException($"Cannot complete service request in status '{sr.Status}'. Only Open or Acknowledged requests can be completed.");
            }

            sr.Complete();

            var nowUtc = DateTimeOffset.UtcNow;
            _outboxWriter?.EnqueueEvent(
                tenantId: tenantId.Value,
                branchId: branchId.Value,
                aggregateId: sr.Id.Value.ToString(),
                eventType: RealtimeEventTypes.TableServiceRequestStatusChanged,
                data: new TableServiceRequestStatusChangedEventData(
                    sr.Id.Value,
                    sr.TableId.Value,
                    sr.DiningSessionId.Value,
                    ServiceRequestStatus.Completed.ToString(),
                    nowUtc),
                idempotencyKey: $"sr-comp-{sr.Id.Value}-{sr.ConcurrencyToken:D}",
                occurredAtUtc: nowUtc);

            var audit = SecurityAuditEvent.Create(
                tenantId,
                SecurityAuditEventType.StaffServiceRequestCompleted,
                nowUtc,
                userId: new UserId(actor.SubjectId),
                branchId: branchId,
                detailsJson: JsonSerializer.Serialize(new
                {
                    requestId = sr.Id.Value,
                    sessionId = sr.DiningSessionId.Value,
                    tableId = sr.TableId.Value,
                    requestType = sr.RequestType.ToString(),
                    actorId = actor.SubjectId,
                    actorRole = actor.Role.ToString()
                }));
            _dbContext.SecurityAuditEvents.Add(audit);

            try
            {
                await _dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ConcurrencyConflictException("Service request was modified concurrently by another transaction.", ex);
            }

            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }

            var tableNumber = await _dbContext.RestaurantTables
                .Where(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == sr.TableId)
                .Select(t => t.TableNumber)
                .FirstOrDefaultAsync(ct);

            return MapToStaffDto(sr, tableNumber ?? string.Empty);
        }
        finally
        {
            if (localTx != null)
            {
                await localTx.DisposeAsync();
            }
        }
    }

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

    private void EnsureServiceRequestManagePermission(AuthenticatedPrincipal actor)
    {
        if (!_permissionRegistry.HasFullGrant(actor.Role, Permissions.ServiceRequestManage))
        {
            throw new InvalidAuthorizationScopeException("Actor lacks permission 'service.request.manage' to manage service requests.");
        }
    }

    private static StaffServiceRequestDto MapToStaffDto(ServiceRequest sr, string tableNumber)
    {
        return new StaffServiceRequestDto(
            sr.Id.Value,
            sr.TenantId.Value,
            sr.BranchId.Value,
            sr.DiningSessionId.Value,
            sr.TableId.Value,
            tableNumber,
            sr.RequestType.ToString(),
            sr.Status.ToString(),
            sr.Note,
            sr.CreatedAtUtc,
            sr.AcknowledgedAtUtc,
            sr.CompletedAtUtc,
            sr.ConcurrencyToken);
    }
}
