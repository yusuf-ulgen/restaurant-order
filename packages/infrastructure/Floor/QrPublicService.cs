using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Floor;

/// <summary>
/// Service implementing customer-facing QR resolution and token exchange flows.
/// Features distributed rate limiting, cryptographic signature verification, table version checking,
/// atomic session acquisition (single active session guarantee), and customer JWT generation.
/// </summary>
public sealed class QrPublicService : IQrPublicService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IQrSecurityService _qrSecurity;
    private readonly IQrRateLimiter _rateLimiter;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ILogger<QrPublicService> _logger;

    public QrPublicService(
        RestaurantOrderDbContext dbContext,
        IQrSecurityService qrSecurity,
        IQrRateLimiter rateLimiter,
        IJwtTokenGenerator jwtTokenGenerator,
        ILogger<QrPublicService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _qrSecurity = qrSecurity ?? throw new ArgumentNullException(nameof(qrSecurity));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _jwtTokenGenerator = jwtTokenGenerator ?? throw new ArgumentNullException(nameof(jwtTokenGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<QrResolveResponse> ResolveQrAsync(string token, string clientIp, CancellationToken ct = default)
    {
        await _rateLimiter.EnsureNotRateLimitedAsync(clientIp, "resolve", ct);

        var payload = _qrSecurity.VerifyAndDecodeToken(token, DateTimeOffset.UtcNow);
        var fingerprint = _qrSecurity.GetFingerprint(token);
        _logger.LogInformation("Resolving QR code with fingerprint {Fingerprint} for table code {TableCode}",
            fingerprint, payload.PublicTableCode);

        // Load table using IgnoreQueryFilters since public endpoints operate pre-auth
        var table = await _dbContext.RestaurantTables
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == payload.TenantId &&
                                      t.BranchId == payload.BranchId &&
                                      t.PublicCode == payload.PublicTableCode, ct)
            ?? throw new QrTableInactiveException($"Table with code '{payload.PublicTableCode}' was not found.");

        if (!table.IsActive)
        {
            throw new QrTableInactiveException($"Table '{table.TableNumber}' is currently inactive.");
        }

        if (table.QrVersion != payload.QrVersion)
        {
            throw new QrRevokedException(
                $"Table QR code has been revoked. Current version is {table.QrVersion}, token has {payload.QrVersion}.");
        }

        if (payload.Mode == QrMode.Dynamic)
        {
            var dynamicSession = await _dbContext.DiningSessions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == payload.TenantId &&
                                          s.BranchId == payload.BranchId &&
                                          s.Id == payload.TableSessionId!.Value, ct)
                ?? throw new QrSessionClosedException("Dining session referenced by dynamic QR does not exist.");

            if (dynamicSession.TableId != table.Id)
            {
                throw new QrSecurityException("Dynamic QR token table does not match session table.");
            }

            if (dynamicSession.Status == DiningSessionStatus.Closed)
            {
                throw new QrSessionClosedException("Dining session is closed.");
            }
        }

        var branch = await _dbContext.Branches
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == payload.TenantId && b.Id == payload.BranchId, ct);

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == payload.TenantId, ct);

        var activeSession = await _dbContext.DiningSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == payload.TenantId &&
                                      s.BranchId == payload.BranchId &&
                                      s.TableId == table.Id &&
                                      s.Status != DiningSessionStatus.Closed, ct);

        return new QrResolveResponse(
            TenantId: payload.TenantId.Value,
            BranchId: payload.BranchId.Value,
            BrandName: tenant?.Name ?? "Restaurant",
            BranchName: branch?.Name ?? "Branch",
            TableNumber: table.TableNumber,
            TableName: table.Name,
            Mode: payload.Mode == QrMode.Static ? "static" : "dynamic",
            HasActiveSession: activeSession != null,
            ActiveSessionStatus: activeSession?.Status.ToString());
    }

    public async Task<QrExchangeResult> ExchangeQrAsync(string token, string clientIp, CancellationToken ct = default)
    {
        await _rateLimiter.EnsureNotRateLimitedAsync(clientIp, "exchange", ct);

        var payload = _qrSecurity.VerifyAndDecodeToken(token, DateTimeOffset.UtcNow);
        var fingerprint = _qrSecurity.GetFingerprint(token);
        _logger.LogInformation("Exchanging QR token with fingerprint {Fingerprint} for table code {TableCode}",
            fingerprint, payload.PublicTableCode);

        QrExchangeResult? result = null;

        // Execute in tenant transaction context to ensure RLS & atomic session creation
        var tx = await _dbContext.BeginTenantTransactionAsync(payload.TenantId.Value, cancellationToken: ct);
        try
        {
            var table = await _dbContext.RestaurantTables
                .FirstOrDefaultAsync(t => t.TenantId == payload.TenantId &&
                                          t.BranchId == payload.BranchId &&
                                          t.PublicCode == payload.PublicTableCode, ct)
                ?? throw new QrTableInactiveException($"Table with code '{payload.PublicTableCode}' was not found.");

            if (!table.IsActive)
            {
                throw new QrTableInactiveException($"Table '{table.TableNumber}' is currently inactive.");
            }

            if (table.QrVersion != payload.QrVersion)
            {
                throw new QrRevokedException(
                    $"Table QR code has been revoked. Current version is {table.QrVersion}, token has {payload.QrVersion}.");
            }

            DiningSession targetSession;

            if (payload.Mode == QrMode.Dynamic)
            {
                var session = await _dbContext.DiningSessions
                    .FirstOrDefaultAsync(s => s.TenantId == payload.TenantId &&
                                              s.BranchId == payload.BranchId &&
                                              s.Id == payload.TableSessionId!.Value, ct)
                    ?? throw new QrSessionClosedException("Dining session referenced by dynamic QR does not exist.");

                if (session.TableId != table.Id)
                {
                    throw new QrSecurityException("Dynamic QR token table does not match session table.");
                }

                if (session.Status == DiningSessionStatus.Closed)
                {
                    throw new QrSessionClosedException("Dining session is closed.");
                }

                targetSession = session;
            }
            else
            {
                // Static QR flow: join existing active session or atomically create new Open session
                var existingSession = await _dbContext.DiningSessions
                    .FirstOrDefaultAsync(s => s.TenantId == payload.TenantId &&
                                              s.BranchId == payload.BranchId &&
                                              s.TableId == table.Id &&
                                              s.Status != DiningSessionStatus.Closed, ct);

                if (existingSession != null)
                {
                    targetSession = existingSession;
                }
                else
                {
                    var newSession = DiningSession.Open(payload.TenantId, payload.BranchId, table, guestCount: 1);
                    _dbContext.DiningSessions.Add(newSession);

                    try
                    {
                        await _dbContext.SaveChangesAsync(ct);
                        targetSession = newSession;
                    }
                    catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                    {
                        // Concurrent race condition: another request created the active session
                        _dbContext.ChangeTracker.Clear();
                        targetSession = await _dbContext.DiningSessions
                            .FirstOrDefaultAsync(s => s.TenantId == payload.TenantId &&
                                                      s.BranchId == payload.BranchId &&
                                                      s.TableId == table.Id &&
                                                      s.Status != DiningSessionStatus.Closed, ct)
                            ?? throw new InvalidOperationException("Failed to acquire dining session during concurrent exchange.");
                    }
                }
            }

            // Issue short-lived Customer JWT Access Token
            var nowUtc = DateTimeOffset.UtcNow;
            var tokenResult = _jwtTokenGenerator.GenerateAccessToken(
                userId: new UserId(targetSession.Id.Value),
                sessionId: targetSession.Id.Value,
                principalType: PrincipalType.Customer,
                role: AuthRole.Customer,
                scope: AuthorizationScope.ForTableSession(payload.TenantId, payload.BranchId, targetSession.Id.Value),
                authMethod: AuthenticationMethod.CustomerQrSession,
                securityVersion: 0,
                nowUtc: nowUtc);

            var auditEvent = SecurityAuditEvent.Create(
                tenantId: payload.TenantId,
                eventType: SecurityAuditEventType.QrExchanged,
                nowUtc: nowUtc,
                userId: null,
                branchId: payload.BranchId,
                detailsJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    tableId = table.Id.Value,
                    tableNumber = table.TableNumber,
                    sessionId = targetSession.Id.Value,
                    mode = payload.Mode.ToString(),
                    fingerprint
                }));

            _dbContext.SecurityAuditEvents.Add(auditEvent);
            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            result = new QrExchangeResult(
                AccessToken: tokenResult.Token,
                AccessTokenExpiresAt: tokenResult.ExpiresAtUtc,
                SessionId: targetSession.Id.Value,
                SessionStatus: targetSession.Status.ToString(),
                TenantId: payload.TenantId.Value,
                BranchId: payload.BranchId.Value,
                TableNumber: table.TableNumber,
                TableName: table.Name);
        }
        finally
        {
            await tx.DisposeAsync();
        }

        return result;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
    }
}
