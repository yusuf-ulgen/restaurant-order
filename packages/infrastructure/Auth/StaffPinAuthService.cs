using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Implements staff 4-digit PIN authentication restricted strictly to enrolled trusted terminals,
/// with progressive brute-force backoff, PIN lockout, and session management.
/// </summary>
public sealed class StaffPinAuthService : IStaffPinAuthService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly ITrustedTerminalService _terminalService;
    private readonly ITerminalPinRateLimiter _rateLimiter;
    private readonly IPinHasher _pinHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IRefreshTokenService _refreshTokenService;

    public StaffPinAuthService(
        RestaurantOrderDbContext dbContext,
        ITrustedTerminalService terminalService,
        ITerminalPinRateLimiter rateLimiter,
        IPinHasher pinHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IRefreshTokenService refreshTokenService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _pinHasher = pinHasher ?? throw new ArgumentNullException(nameof(pinHasher));
        _jwtTokenGenerator = jwtTokenGenerator ?? throw new ArgumentNullException(nameof(jwtTokenGenerator));
        _refreshTokenService = refreshTokenService ?? throw new ArgumentNullException(nameof(refreshTokenService));
    }

    public async Task<AuthResult> LoginWithPinAsync(
        PinLoginCommand command,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Step 1: Terminal Authentication
        var terminal = await _terminalService.AuthenticateTerminalAsync(command.TerminalId, command.DeviceSecret, ct);
        if (terminal == null || !terminal.IsActive)
        {
            throw new PinAuthFailureException("Terminal authentication failed.");
        }

        // Step 2: Rate limit check on terminal and user
        var rateLimit = await _rateLimiter.CheckAttemptAllowedAsync(command.TerminalId, command.UserId, ct);
        if (rateLimit.IsLockedOut)
        {
            throw new AuthRateLimitException(rateLimit.RetryAfterSeconds, rateLimit.Message ?? "Terminal locked.");
        }

        if (rateLimit.BackoffDelay.HasValue && rateLimit.BackoffDelay.Value > TimeSpan.Zero)
        {
            await Task.Delay(rateLimit.BackoffDelay.Value, ct);
        }

        // Step 3: Fetch user
        User? user = null;
        if (command.UserId.HasValue)
        {
            var userId = new UserId(command.UserId.Value);
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        }
        else if (!string.IsNullOrWhiteSpace(command.Email))
        {
            var normalizedEmail = User.NormalizeEmail(command.Email);
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);
        }

        if (user == null || user.Status != UserStatus.Active || user.IsLockedOut(now))
        {
            await _rateLimiter.RecordFailedAttemptAsync(command.TerminalId, command.UserId, ct);
            throw new PinAuthFailureException("User not found or inactive.");
        }

        // Step 4: Fetch user membership for this terminal's tenant & branch
        var tenantId = TenantId.From(terminal.TenantId);
        var branchId = BranchId.From(terminal.BranchId);

        var membership = await _dbContext.Memberships
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.TenantId == tenantId && m.BranchId == branchId && m.IsActive, ct);

        if (membership == null)
        {
            await _rateLimiter.RecordFailedAttemptAsync(command.TerminalId, user.Id.Value, ct);
            throw new PinAuthFailureException("No active membership in branch.");
        }

        // Step 5: Fetch PIN credential
        var pinCred = await _dbContext.PinCredentials
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.BranchId == branchId && p.UserId == user.Id, ct);

        if (pinCred == null)
        {
            await _rateLimiter.RecordFailedAttemptAsync(command.TerminalId, user.Id.Value, ct);
            throw new PinAuthFailureException("No PIN configured for user in this branch.");
        }

        if (pinCred.IsLocked(now))
        {
            await _rateLimiter.RecordFailedAttemptAsync(command.TerminalId, user.Id.Value, ct);
            throw new PinAuthFailureException("PIN credential is temporarily locked.");
        }

        // Step 6: Verify PIN
        var verifyResult = _pinHasher.VerifyPin(command.Pin, pinCred.PinHash, pinCred.PepperKeyId);
        if (verifyResult == PinVerificationResult.Failed)
        {
            pinCred.RecordFailedAttempt(5, TimeSpan.FromMinutes(15), now);
            await _rateLimiter.RecordFailedAttemptAsync(command.TerminalId, user.Id.Value, ct);

            var failAudit = SecurityAuditEvent.Create(
                tenantId: tenantId,
                eventType: SecurityAuditEventType.LoginFailed,
                nowUtc: now,
                userId: user.Id,
                branchId: branchId,
                ipAddress: command.IpAddress,
                userAgent: $"terminal:{terminal.TerminalId}",
                detailsJson: $"{{\"terminal\":\"{terminal.TerminalName}\"}}");
            _dbContext.SecurityAuditEvents.Add(failAudit);
            await _dbContext.SaveChangesAsync(ct);

            throw new PinAuthFailureException("Invalid PIN.");
        }

        // Success: Reset rate limiter and pin attempts
        pinCred.RecordSuccessfulAttempt(now);
        await _rateLimiter.ResetAttemptsAsync(command.TerminalId, user.Id.Value, ct);

        // Step 7: Create session
        var sessionLifetime = TimeSpan.FromHours(8);
        var session = AuthSession.Create(
            tenantId: tenantId,
            userId: user.Id,
            membershipId: membership.Id,
            branchId: branchId,
            authMethod: AuthenticationMethod.Pin,
            sessionLifetime: sessionLifetime,
            nowUtc: now,
            ipAddress: command.IpAddress,
            userAgent: $"terminal:{terminal.TerminalId}");

        _dbContext.Sessions.Add(session);

        // Step 8: Generate Access Token
        var scope = AuthorizationScope.ForBranch(tenantId, branchId);
        var tokenResult = _jwtTokenGenerator.GenerateAccessToken(
            userId: user.Id,
            sessionId: session.Id,
            principalType: PrincipalType.Staff,
            role: membership.Role,
            scope: scope,
            authMethod: AuthenticationMethod.Pin,
            securityVersion: user.SecurityVersion,
            nowUtc: now);

        var rawRefreshToken = _refreshTokenService.GenerateOpaqueToken();
        var tokenHash = _refreshTokenService.HashToken(rawRefreshToken);
        var familyId = Guid.CreateVersion7();
        var refreshToken = RefreshToken.Create(
            tenantId: tenantId,
            sessionId: session.Id,
            tokenFamilyId: familyId,
            tokenHash: tokenHash,
            lifetime: sessionLifetime,
            nowUtc: now);
        _dbContext.RefreshTokens.Add(refreshToken);

        var successAudit = SecurityAuditEvent.Create(
            tenantId: tenantId,
            eventType: SecurityAuditEventType.LoginSucceeded,
            nowUtc: now,
            userId: user.Id,
            branchId: branchId,
            ipAddress: command.IpAddress,
            userAgent: $"terminal:{terminal.TerminalId}",
            detailsJson: $"{{\"terminal\":\"{terminal.TerminalName}\"}}");
        _dbContext.SecurityAuditEvents.Add(successAudit);

        await _dbContext.SaveChangesAsync(ct);

        var userDto = new UserPrincipalDto(
            UserId: user.Id.Value,
            Email: user.Email,
            Role: membership.Role.ToString(),
            TenantId: tenantId.Value,
            BranchId: branchId.Value,
            SecurityVersion: user.SecurityVersion);

        var sessionExpiresAt = now.Add(sessionLifetime);
        var sessionDto = new SessionDto(
            Id: session.Id,
            AuthMethod: session.AuthMethod.ToString(),
            SessionState: "Active",
            CreatedAtUtc: session.CreatedAtUtc,
            LastActivityUtc: session.LastSeenAtUtc,
            ExpiresAtUtc: session.ExpiresAtUtc,
            IsCurrent: true);

        return new AuthResult(
            AccessToken: tokenResult.Token,
            RefreshToken: rawRefreshToken,
            AccessTokenExpiresAt: tokenResult.ExpiresAtUtc,
            RefreshTokenExpiresAt: sessionExpiresAt,
            User: userDto,
            Session: sessionDto);
    }

    public async Task SetPinAsync(
        TenantId tenantId,
        SetStaffPinCommand command,
        UserId actorUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Pin) || command.Pin.Length != 4 || !command.Pin.All(char.IsDigit))
        {
            throw new ArgumentException("Staff PIN must be exactly 4 numeric digits.", nameof(command));
        }

        var now = DateTimeOffset.UtcNow;
        var targetUserId = new UserId(command.TargetUserId);
        var branchId = new BranchId(command.BranchId);

        var hashResult = _pinHasher.HashPin(command.Pin);

        var existing = await _dbContext.PinCredentials
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == targetUserId && p.BranchId == branchId, ct);

        if (existing != null)
        {
            existing.UpdatePin(hashResult.Hash, hashResult.AlgorithmVersion, hashResult.PepperKeyId, now);
        }
        else
        {
            var pinCred = PinCredential.Create(
                tenantId: tenantId,
                userId: targetUserId,
                branchId: branchId,
                pinHash: hashResult.Hash,
                algorithmVersion: hashResult.AlgorithmVersion,
                pepperKeyId: hashResult.PepperKeyId,
                nowUtc: now);

            _dbContext.PinCredentials.Add(pinCred);
        }

        var auditEvent = SecurityAuditEvent.Create(
            tenantId: tenantId,
            eventType: SecurityAuditEventType.PinChanged,
            nowUtc: now,
            userId: actorUserId,
            branchId: branchId,
            ipAddress: null,
            userAgent: null,
            detailsJson: $"{{\"targetUserId\":\"{command.TargetUserId}\",\"branchId\":\"{command.BranchId}\"}}");

        _dbContext.SecurityAuditEvents.Add(auditEvent);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task LogoutPinSessionAsync(
        Guid sessionId,
        Guid terminalId,
        CancellationToken ct = default)
    {
        var session = await _dbContext.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session != null && !session.IsRevoked)
        {
            session.Revoke($"PIN session quick-logout from terminal {terminalId}", DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(ct);
        }
    }
}
