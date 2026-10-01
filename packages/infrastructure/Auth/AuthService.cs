using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Core authentication service governing user login, password verification,
/// rotating refresh tokens, and rate limits across platform and tenant boundaries.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IIamUserLookupGateway _userLookupGateway;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IPlatformSessionStore _platformSessionStore;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly IAuthSessionManager _sessionManager;
    private readonly AuthSettings _authSettings;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        RestaurantOrderDbContext dbContext,
        IIamUserLookupGateway userLookupGateway,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IRefreshTokenService refreshTokenService,
        IPlatformSessionStore platformSessionStore,
        ILoginRateLimiter rateLimiter,
        IAuthSessionManager sessionManager,
        IOptions<AuthSettings> authSettings,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _userLookupGateway = userLookupGateway ?? throw new ArgumentNullException(nameof(userLookupGateway));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _jwtTokenGenerator = jwtTokenGenerator ?? throw new ArgumentNullException(nameof(jwtTokenGenerator));
        _refreshTokenService = refreshTokenService ?? throw new ArgumentNullException(nameof(refreshTokenService));
        _platformSessionStore = platformSessionStore ?? throw new ArgumentNullException(nameof(platformSessionStore));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _authSettings = authSettings?.Value ?? new AuthSettings();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AuthResult> LoginAsync(LoginCommand command, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedEmail = User.NormalizeEmail(command.Email);
        var ip = command.IpAddress ?? "unknown";

        if (await _rateLimiter.IsRateLimitedAsync(ip, normalizedEmail, command.TenantSlug, ct))
        {
            throw new AuthRateLimitException(60);
        }

        var lookupUser = await _userLookupGateway.LookupUserForLoginAsync(normalizedEmail, ct);
        if (lookupUser == null)
        {
            await _rateLimiter.RecordFailedAttemptAsync(ip, normalizedEmail, command.TenantSlug, ct);
            _passwordHasher.VerifyPassword(command.Password, "AQAAAAIAAYagAAAAEO5dummyhashdummyhashdummyhashdummyhash==");
            throw new AuthFailureException("User not found.");
        }

        if (lookupUser.Status == (int)UserStatus.Locked && lookupUser.LockoutEndUtc.HasValue && now < lookupUser.LockoutEndUtc.Value)
        {
            await _rateLimiter.RecordFailedAttemptAsync(ip, normalizedEmail, command.TenantSlug, ct);
            throw new AuthFailureException("Account is locked.");
        }

        if (lookupUser.Status != (int)UserStatus.Active && lookupUser.Status != (int)UserStatus.Locked)
        {
            await _rateLimiter.RecordFailedAttemptAsync(ip, normalizedEmail, command.TenantSlug, ct);
            throw new AuthFailureException("Account is inactive.");
        }

        var verificationResult = _passwordHasher.VerifyPassword(command.Password, lookupUser.PasswordHash);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            await _rateLimiter.RecordFailedAttemptAsync(ip, normalizedEmail, command.TenantSlug, ct);
            await RecordFailedLoginAsync(lookupUser.UserId, now, ct);
            throw new AuthFailureException("Invalid credentials.");
        }

        var isSuperAdmin = _authSettings.SuperAdminEmails.Any(e => e.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(command.TenantSlug))
        {
            if (!isSuperAdmin)
            {
                await _rateLimiter.RecordFailedAttemptAsync(ip, normalizedEmail, command.TenantSlug, ct);
                throw new AuthFailureException("Tenant context required for non-superadmin.");
            }

            return await HandleSuperAdminLoginAsync(lookupUser, command, verificationResult, now, ct);
        }

        return await HandleTenantStaffLoginAsync(lookupUser, command, verificationResult, now, ct);
    }

    private async Task<AuthResult> HandleSuperAdminLoginAsync(
        UserLoginLookupDto lookupUser,
        LoginCommand command,
        PasswordVerificationResult verificationResult,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await _dbContext.Users.FirstAsync(u => u.Id == new UserId(lookupUser.UserId), ct);
        user.RecordSuccessfulLogin(now);
        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.ChangePassword(_passwordHasher.HashPassword(command.Password).Hash, now);
        }

        await _dbContext.SaveChangesAsync(ct);
        await _rateLimiter.ResetAttemptsAsync(command.IpAddress ?? "unknown", user.NormalizedEmail, null, ct);

        var sessionId = Guid.CreateVersion7();
        var familyId = Guid.CreateVersion7();
        var rawRefreshToken = _refreshTokenService.GenerateOpaqueToken();
        var tokenHash = _refreshTokenService.HashToken(rawRefreshToken);
        var sessionLifetime = TimeSpan.FromDays(_authSettings.RefreshTokenLifetimeDays);

        await _platformSessionStore.CreateSessionAsync(user.Id, sessionId, familyId, tokenHash, sessionLifetime, now);

        var scope = AuthorizationScope.Platform();
        var tokenResult = _jwtTokenGenerator.GenerateAccessToken(
            user.Id,
            sessionId,
            PrincipalType.Staff,
            AuthRole.SuperAdmin,
            scope,
            AuthenticationMethod.Password,
            user.SecurityVersion,
            now);

        var sessionExpiresAt = now.Add(sessionLifetime);
        return new AuthResult(
            AccessToken: tokenResult.Token,
            RefreshToken: rawRefreshToken,
            AccessTokenExpiresAt: tokenResult.ExpiresAtUtc,
            RefreshTokenExpiresAt: sessionExpiresAt,
            User: new UserPrincipalDto(user.Id.Value, user.Email, AuthRole.SuperAdmin.ToString(), null, null, user.SecurityVersion),
            Session: new SessionDto(sessionId, "Password", "Active", now, now, sessionExpiresAt, IsCurrent: true));
    }

    private async Task<AuthResult> HandleTenantStaffLoginAsync(
        UserLoginLookupDto lookupUser,
        LoginCommand command,
        PasswordVerificationResult verificationResult,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Slug == command.TenantSlug, ct);
        if (tenant == null || tenant.Status != TenantStatus.Active)
        {
            await _rateLimiter.RecordFailedAttemptAsync(command.IpAddress ?? "unknown", lookupUser.NormalizedEmail, command.TenantSlug, ct);
            throw new AuthFailureException("Tenant not found or inactive.");
        }

        var membership = await _dbContext.Memberships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenant.Id && m.UserId == new UserId(lookupUser.UserId) && m.IsActive, ct);

        if (membership == null)
        {
            await _rateLimiter.RecordFailedAttemptAsync(command.IpAddress ?? "unknown", lookupUser.NormalizedEmail, command.TenantSlug, ct);
            throw new AuthFailureException("No active membership in tenant.");
        }

        var user = await _dbContext.Users.FirstAsync(u => u.Id == new UserId(lookupUser.UserId), ct);
        user.RecordSuccessfulLogin(now);
        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.ChangePassword(_passwordHasher.HashPassword(command.Password).Hash, now);
        }

        var sessionLifetime = TimeSpan.FromDays(_authSettings.RefreshTokenLifetimeDays);
        var session = AuthSession.Create(
            tenant.Id,
            user.Id,
            membership.Id,
            membership.BranchId,
            AuthenticationMethod.Password,
            sessionLifetime,
            now,
            command.IpAddress,
            command.UserAgent);
        _dbContext.Sessions.Add(session);

        var rawRefreshToken = _refreshTokenService.GenerateOpaqueToken();
        var tokenHash = _refreshTokenService.HashToken(rawRefreshToken);
        var familyId = Guid.CreateVersion7();
        var refreshToken = RefreshToken.Create(tenant.Id, session.Id, familyId, tokenHash, sessionLifetime, now);
        _dbContext.RefreshTokens.Add(refreshToken);

        var audit = SecurityAuditEvent.Create(
            tenant.Id,
            SecurityAuditEventType.LoginSucceeded,
            now,
            user.Id,
            membership.BranchId,
            command.IpAddress,
            command.UserAgent);
        _dbContext.SecurityAuditEvents.Add(audit);

        await _dbContext.SaveChangesAsync(ct);
        await _rateLimiter.ResetAttemptsAsync(command.IpAddress ?? "unknown", user.NormalizedEmail, command.TenantSlug, ct);

        var scope = membership.Role == AuthRole.RestaurantAdmin
            ? AuthorizationScope.ForTenant(tenant.Id)
            : AuthorizationScope.ForBranch(tenant.Id, membership.BranchId!.Value);

        var tokenResult = _jwtTokenGenerator.GenerateAccessToken(
            user.Id,
            session.Id,
            PrincipalType.Staff,
            membership.Role,
            scope,
            AuthenticationMethod.Password,
            user.SecurityVersion,
            now);

        return new AuthResult(
            AccessToken: tokenResult.Token,
            RefreshToken: rawRefreshToken,
            AccessTokenExpiresAt: tokenResult.ExpiresAtUtc,
            RefreshTokenExpiresAt: refreshToken.ExpiresAtUtc,
            User: new UserPrincipalDto(user.Id.Value, user.Email, membership.Role.ToString(), tenant.Id.Value, membership.BranchId?.Value, user.SecurityVersion),
            Session: new SessionDto(session.Id, "Password", "Active", now, now, session.ExpiresAtUtc, IsCurrent: true));
    }

    public async Task<AuthResult> RefreshSessionAsync(string rawRefreshToken, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            throw new AuthFailureException("Refresh token is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var tokenHash = _refreshTokenService.HashToken(rawRefreshToken);
        var newRawToken = _refreshTokenService.GenerateOpaqueToken();
        var newTokenHash = _refreshTokenService.HashToken(newRawToken);
        var tokenLifetime = TimeSpan.FromDays(_authSettings.RefreshTokenLifetimeDays);

        var (isPlatformSuccess, isReuse, platformSession) = await _platformSessionStore.RotateTokenAsync(tokenHash, newTokenHash, tokenLifetime, now);
        if (isPlatformSuccess && platformSession != null)
        {
            var pUser = await _dbContext.Users.FirstAsync(u => u.Id == platformSession.UserId, ct);
            if (pUser.Status != UserStatus.Active) throw new AuthFailureException("User account is not active.");

            var pTokenResult = _jwtTokenGenerator.GenerateAccessToken(
                pUser.Id,
                platformSession.SessionId,
                PrincipalType.Staff,
                AuthRole.SuperAdmin,
                AuthorizationScope.Platform(),
                AuthenticationMethod.Password,
                pUser.SecurityVersion,
                now);

            return new AuthResult(
                AccessToken: pTokenResult.Token,
                RefreshToken: newRawToken,
                AccessTokenExpiresAt: pTokenResult.ExpiresAtUtc,
                RefreshTokenExpiresAt: now.Add(tokenLifetime),
                User: new UserPrincipalDto(pUser.Id.Value, pUser.Email, AuthRole.SuperAdmin.ToString(), null, null, pUser.SecurityVersion),
                Session: new SessionDto(platformSession.SessionId, "Password", "Active", platformSession.CreatedAtUtc, now, platformSession.ExpiresAtUtc, IsCurrent: true));
        }

        if (isReuse) throw new AuthFailureException("Refresh token reuse detected.");

        var token = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (token == null || !token.IsActive(now))
        {
            if (token != null && (token.IsRevoked || token.ReplacedByTokenId.HasValue))
            {
                await _sessionManager.HandleTokenReuseAsync(token, now, ct);
            }
            throw new AuthFailureException("Invalid or expired refresh token.");
        }

        var session = await _dbContext.Sessions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == token.SessionId, ct);
        if (session == null || !session.IsActive(now)) throw new AuthFailureException("Session is revoked or expired.");

        var user = await _dbContext.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == session.UserId, ct);
        if (user == null || user.Status != UserStatus.Active) throw new AuthFailureException("User is inactive.");

        var membership = await _dbContext.Memberships.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == session.MembershipId, ct);
        if (membership == null || !membership.IsActive) throw new AuthFailureException("Membership is inactive.");

        var newRefreshToken = RefreshToken.Create(token.TenantId, session.Id, token.TokenFamilyId, newTokenHash, tokenLifetime, now);
        token.Revoke(now, newRefreshToken.Id);
        session.RecordActivity(now);
        _dbContext.RefreshTokens.Add(newRefreshToken);

        await _dbContext.SaveChangesAsync(ct);

        var scope = membership.Role == AuthRole.RestaurantAdmin
            ? AuthorizationScope.ForTenant(token.TenantId)
            : AuthorizationScope.ForBranch(token.TenantId, membership.BranchId!.Value);

        var tokenResult = _jwtTokenGenerator.GenerateAccessToken(
            user.Id,
            session.Id,
            PrincipalType.Staff,
            membership.Role,
            scope,
            session.AuthMethod,
            user.SecurityVersion,
            now);

        return new AuthResult(
            AccessToken: tokenResult.Token,
            RefreshToken: newRawToken,
            AccessTokenExpiresAt: tokenResult.ExpiresAtUtc,
            RefreshTokenExpiresAt: newRefreshToken.ExpiresAtUtc,
            User: new UserPrincipalDto(user.Id.Value, user.Email, membership.Role.ToString(), token.TenantId.Value, membership.BranchId?.Value, user.SecurityVersion),
            Session: new SessionDto(session.Id, session.AuthMethod.ToString(), "Active", session.CreatedAtUtc, now, session.ExpiresAtUtc, IsCurrent: true));
    }

    public Task LogoutAsync(Guid sessionId, CancellationToken ct = default) =>
        _sessionManager.LogoutAsync(sessionId, ct);

    public Task LogoutAllAsync(UserId userId, CancellationToken ct = default) =>
        _sessionManager.LogoutAllAsync(userId, ct);

    public Task RevokeSessionAsync(UserId currentUserId, Guid targetSessionId, CancellationToken ct = default) =>
        _sessionManager.RevokeSessionAsync(currentUserId, targetSessionId, ct);

    public Task<IReadOnlyList<SessionDto>> GetActiveSessionsAsync(UserId userId, Guid? currentSessionId = null, CancellationToken ct = default) =>
        _sessionManager.GetActiveSessionsAsync(userId, currentSessionId, ct);

    private async Task RecordFailedLoginAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == new UserId(userId), ct);
        if (user != null)
        {
            user.RecordFailedLogin(
                maxAttempts: _authSettings.MaxFailedLoginAttempts,
                lockoutDuration: TimeSpan.FromMinutes(_authSettings.LockoutMinutes),
                nowUtc: now);

            if (user.Status == UserStatus.Locked)
            {
                _logger.LogWarning("Account {UserId} locked due to exceeding maximum failed login attempts.", user.Id);
            }

            await _dbContext.SaveChangesAsync(ct);
        }
    }
}
