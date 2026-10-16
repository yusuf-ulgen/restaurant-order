using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
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
/// Infrastructure implementation of customer service requests.
/// Enforces PostgreSQL forced RLS, own-session customer scopes, rate limiting,
/// idempotent replays, and partial-unique open request constraints.
/// </summary>
public class CustomerServiceRequestService : ICustomerServiceRequestService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IServiceRequestRateLimiter _rateLimiter;

    public CustomerServiceRequestService(
        RestaurantOrderDbContext dbContext,
        IServiceRequestRateLimiter rateLimiter)
    {
        _dbContext = dbContext;
        _rateLimiter = rateLimiter;
    }

    public async Task<ServiceRequestDto> CreateRequestAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        CreateCustomerServiceRequest request,
        string idempotencyKey,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureCustomerOwnSession(tenantId, branchId, sessionId, actor);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        var trimmedKey = idempotencyKey.Trim();
        if (trimmedKey.Length > ServiceRequest.MaxIdempotencyKeyLength)
        {
            throw new ArgumentException(
                $"Idempotency key cannot exceed {ServiceRequest.MaxIdempotencyKeyLength} characters.",
                nameof(idempotencyKey));
        }


        // 2. Execute within tenant transaction under PostgreSQL RLS
        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(tenantId.Value, cancellationToken: ct);
        }

        try
        {
            // Advisory lock on idempotency key to prevent race conditions on concurrent identical keys
            if (_dbContext.Database.IsNpgsql())
        {
            var lockKey = $"sr-idem:{tenantId.Value:N}:{sessionId.Value:N}:{trimmedKey}";
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", ct);
        }

        // 3. Verify session state from database source of truth
        var session = await _dbContext.DiningSessions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == sessionId, ct)
            ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found in branch '{branchId.Value}'.");

        if (session.Status == DiningSessionStatus.Closed || session.MergedIntoSessionId.HasValue)
        {
            throw new DomainException("Cannot submit service request for a closed or merged dining session.");
        }

        if (session.Status == DiningSessionStatus.BillRequested)
        {
            throw new DomainException("Cannot submit service request for a dining session with bill requested.");
        }

        var sanitizedNote = ServiceRequest.ValidateAndSanitizeNote(request.Note);
        var expectedFingerprint = ServiceRequest.ComputeFingerprint(
            tenantId,
            branchId,
            sessionId,
            session.TableId,
            request.RequestType,
            sanitizedNote);

        // 4. Idempotency Check: Existing key check
        var existingWithKey = await _dbContext.ServiceRequests
            .FirstOrDefaultAsync(sr => sr.TenantId == tenantId &&
                                       sr.BranchId == branchId &&
                                       sr.IdempotencyKey == trimmedKey, ct);

        if (existingWithKey != null)
        {
            if (!ServiceRequest.MatchesFingerprint(existingWithKey.RequestFingerprint, expectedFingerprint) ||
                existingWithKey.DiningSessionId != sessionId)
            {
                throw new ConcurrencyConflictException(
                    $"Idempotency key '{trimmedKey}' was already used with a different request payload or session.");
            }

            // Replay matching idempotency record
            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }
            return MapToDto(existingWithKey);
        }

        // 5. Open request deduplication: if an open request of the same type already exists, return it safely
        var existingOpen = await _dbContext.ServiceRequests
            .FirstOrDefaultAsync(sr => sr.TenantId == tenantId &&
                                       sr.DiningSessionId == sessionId &&
                                       sr.RequestType == request.RequestType &&
                                       sr.Status == ServiceRequestStatus.Open, ct);

        if (existingOpen != null)
        {
            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }
            return MapToDto(existingOpen);
        }

        // 6. Rate limiting & spam prevention (enforced on new request creations)
        await _rateLimiter.EnsureNotRateLimitedAsync(tenantId.Value, sessionId.Value, ct);

        // 7. Create and persist new service request
        var serviceRequest = ServiceRequest.Create(
            tenantId,
            branchId,
            sessionId,
            session.TableId,
            request.RequestType,
            sanitizedNote,
            trimmedKey);

        _dbContext.ServiceRequests.Add(serviceRequest);

        // 7. Security audit event
        var audit = SecurityAuditEvent.Create(
            tenantId,
            SecurityAuditEventType.CustomerServiceRequestCreated,
            DateTimeOffset.UtcNow,
            userId: null,
            branchId: branchId,
            detailsJson: JsonSerializer.Serialize(new
            {
                requestId = serviceRequest.Id.Value,
                sessionId = sessionId.Value,
                tableId = session.TableId.Value,
                requestType = serviceRequest.RequestType.ToString(),
                note = serviceRequest.Note
            }));
        _dbContext.SecurityAuditEvents.Add(audit);

            await _dbContext.SaveChangesAsync(ct);
            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }

            return MapToDto(serviceRequest);
        }
        finally
        {
            if (localTx != null)
            {
                await localTx.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<ServiceRequestDto>> GetOpenRequestsAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureCustomerOwnSession(tenantId, branchId, sessionId, actor);

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(tenantId.Value, cancellationToken: ct);
        }

        try
        {
            var list = await _dbContext.ServiceRequests
                .AsNoTracking()
                .Where(sr => sr.TenantId == tenantId &&
                             sr.BranchId == branchId &&
                             sr.DiningSessionId == sessionId &&
                             sr.Status == ServiceRequestStatus.Open)
                .OrderBy(sr => sr.CreatedAtUtc)
                .ToListAsync(ct);

            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }

            return list.Select(MapToDto).ToList();
        }
        finally
        {
            if (localTx != null)
            {
                await localTx.DisposeAsync();
            }
        }
    }

    public async Task<ServiceRequestDto> CancelRequestAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        ServiceRequestId requestId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureCustomerOwnSession(tenantId, branchId, sessionId, actor);

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _dbContext.BeginTenantTransactionAsync(tenantId.Value, cancellationToken: ct);
        }

        try
        {
            // Non-leaking resource lookup: requires tenant, branch, session, and request id match
            var sr = await _dbContext.ServiceRequests
                .FirstOrDefaultAsync(x => x.Id == requestId &&
                                          x.TenantId == tenantId &&
                                          x.BranchId == branchId &&
                                          x.DiningSessionId == sessionId, ct)
                ?? throw new ResourceNotFoundException($"Service request '{requestId.Value}' was not found.");

            sr.Cancel();

            var audit = SecurityAuditEvent.Create(
                tenantId,
                SecurityAuditEventType.CustomerServiceRequestCancelled,
                DateTimeOffset.UtcNow,
                userId: null,
                branchId: branchId,
                detailsJson: JsonSerializer.Serialize(new
                {
                    requestId = sr.Id.Value,
                    sessionId = sessionId.Value,
                    tableId = sr.TableId.Value,
                    requestType = sr.RequestType.ToString()
                }));
            _dbContext.SecurityAuditEvents.Add(audit);

            await _dbContext.SaveChangesAsync(ct);
            if (localTx != null)
            {
                await localTx.CommitAsync(ct);
            }

            return MapToDto(sr);
        }
        finally
        {
            if (localTx != null)
            {
                await localTx.DisposeAsync();
            }
        }
    }

    private static void EnsureCustomerOwnSession(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        AuthenticatedPrincipal actor)
    {
        if (actor.Role != AuthRole.Customer)
        {
            throw new InvalidAuthorizationScopeException("Only customers may manage session service requests.");
        }

        if (actor.Scope.TenantId != tenantId.Value ||
            actor.Scope.BranchId != branchId.Value ||
            !actor.Scope.TableSessionId.HasValue ||
            actor.Scope.TableSessionId.Value != sessionId.Value)
        {
            throw new InvalidAuthorizationScopeException("Customer is not authorized to access foreign dining session.");
        }
    }

    private static ServiceRequestDto MapToDto(ServiceRequest sr)
    {
        return new ServiceRequestDto(
            sr.Id.Value,
            sr.TenantId.Value,
            sr.BranchId.Value,
            sr.DiningSessionId.Value,
            sr.TableId.Value,
            sr.RequestType.ToString(),
            sr.Status.ToString(),
            sr.Note,
            sr.IdempotencyKey,
            sr.CreatedAtUtc,
            sr.AcknowledgedAtUtc,
            sr.CompletedAtUtc,
            sr.ConcurrencyToken);
    }
}
