using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
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
    private readonly IIamBootstrapGateway _bootstrapGateway;
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
        IIamBootstrapGateway bootstrapGateway,
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
        _bootstrapGateway = bootstrapGateway ?? throw new ArgumentNullException(nameof(bootstrapGateway));
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
            throw new AuthFailureException("Invalid credentials.");
        }

        if (lookupUser.Status == (int)UserStatus.Locked && lookupUser.LockoutEndUtc.HasValue && now < lookupUser.LockoutEndUtc.Value)
        {
            await _rateLimiter.RecordFailedAttemptAsync(ip, normalizedEmail, command.TenantSlug, ct);
            throw new AuthFailureException("Invalid credentials.");
        }

        if (lookupUser.Status != (int)UserStatus.Active && lookupUser.Status != (int)UserStatus.Locked)
        {
            await _rateLimiter.RecordFailedAttemptAsync(ip, normalizedEmail, command.TenantSlug, ct);
            throw new AuthFailureException("Invalid credentials.");
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
                throw new AuthFailureException("Invalid credentials.");
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
        string? newPasswordHash = verificationResult == PasswordVerificationResult.SuccessRehashNeeded
            ? _passwordHasher.HashPassword(command.Password).Hash
            : null;

        await _bootstrapGateway.RecordSuccessfulLoginAsync(lookupUser.UserId, now, newPasswordHash, ct);
        await _rateLimiter.ResetAttemptsAsync(command.IpAddress ?? "unknown", lookupUser.NormalizedEmail, null, ct);

        var sessionId = Guid.CreateVersion7();
        var familyId = Guid.CreateVersion7();
        var rawRefreshToken = _refreshTokenService.GenerateOpaqueToken();
        var tokenHash = _refreshTokenService.HashToken(rawRefreshToken);
        var sessionLifetime = TimeSpan.FromDays(_authSettings.RefreshTokenLifetimeDays);

        await _platformSessionStore.CreateSessionAsync(UserId.From(lookupUser.UserId), sessionId, familyId, tokenHash, sessionLifetime, now);

        var scope = AuthorizationScope.Platform();
        var tokenResult = _jwtTokenGenerator.GenerateAccessToken(
            UserId.From(lookupUser.UserId),
            sessionId,
            PrincipalType.Staff,
            AuthRole.SuperAdmin,
            scope,
            AuthenticationMethod.Password,
            lookupUser.SecurityVersion,
            now);

        var sessionExpiresAt = now.Add(sessionLifetime);
        return new AuthResult(
            AccessToken: tokenResult.Token,
            RefreshToken: rawRefreshToken,
            AccessTokenExpiresAt: tokenResult.ExpiresAtUtc,
            RefreshTokenExpiresAt: sessionExpiresAt,
            User: new UserPrincipalDto(lookupUser.UserId, lookupUser.NormalizedEmail, AuthRole.SuperAdmin.ToString(), null, null, lookupUser.SecurityVersion),
            Session: new SessionDto(sessionId, "Password", "Active", now, now, sessionExpiresAt, IsCurrent: true));
    }

    private async Task<AuthResult> HandleTenantStaffLoginAsync(
        UserLoginLookupDto lookupUser,
        LoginCommand command,
        PasswordVerificationResult verificationResult,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var tenant = await _bootstrapGateway.LookupTenantBySlugAsync(command.TenantSlug!, ct);
        if (tenant == null || tenant.Status != (int)TenantStatus.Active)
        {
            await _rateLimiter.RecordFailedAttemptAsync(command.IpAddress ?? "unknown", lookupUser.NormalizedEmail, command.TenantSlug, ct);
            throw new AuthFailureException("Invalid credentials.");
        }

        var membership = await _bootstrapGateway.LookupMembershipForLoginAsync(tenant.TenantId, lookupUser.UserId, ct);
        if (membership == null || !membership.IsActive)
        {
            await _rateLimiter.RecordFailedAttemptAsync(command.IpAddress ?? "unknown", lookupUser.NormalizedEmail, command.TenantSlug, ct);
            throw new AuthFailureException("Invalid credentials.");
        }

        if (!Enum.TryParse<AuthRole>(membership.Role, out var role))
        {
            await _rateLimiter.RecordFailedAttemptAsync(command.IpAddress ?? "unknown", lookupUser.NormalizedEmail, command.TenantSlug, ct);
            throw new AuthFailureException("Invalid credentials.");
        }

        await using var tx = await _dbContext.BeginTenantTransactionAsync(tenant.TenantId, cancellationToken: ct);

        string? newPasswordHash = verificationResult == PasswordVerificationResult.SuccessRehashNeeded
            ? _passwordHasher.HashPassword(command.Password).Hash
            : null;

        await _bootstrapGateway.RecordSuccessfulLoginAsync(lookupUser.UserId, now, newPasswordHash, ct);

        var sessionLifetime = TimeSpan.FromDays(_authSettings.RefreshTokenLifetimeDays);
        var session = AuthSession.Create(
            TenantId.From(tenant.TenantId),
            UserId.From(lookupUser.UserId),
            membership.MembershipId,
            membership.BranchId.HasValue ? BranchId.From(membership.BranchId.Value) : null,
            AuthenticationMethod.Password,
            sessionLifetime,
            now,
            command.IpAddress,
            command.UserAgent);
        _dbContext.Sessions.Add(session);

        var rawRefreshToken = _refreshTokenService.GenerateOpaqueToken();
        var tokenHash = _refreshTokenService.HashToken(rawRefreshToken);
        var familyId = Guid.CreateVersion7();
        var refreshToken = RefreshToken.Create(TenantId.From(tenant.TenantId), session.Id, familyId, tokenHash, sessionLifetime, now);
        _dbContext.RefreshTokens.Add(refreshToken);

        var audit = SecurityAuditEvent.Create(
            TenantId.From(tenant.TenantId),
            SecurityAuditEventType.LoginSucceeded,
            now,
            UserId.From(lookupUser.UserId),
            membership.BranchId.HasValue ? BranchId.From(membership.BranchId.Value) : null,
            command.IpAddress,
            command.UserAgent);
        _dbContext.SecurityAuditEvents.Add(audit);

        await _dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _rateLimiter.ResetAttemptsAsync(command.IpAddress ?? "unknown", lookupUser.NormalizedEmail, command.TenantSlug, ct);

        var scope = role == AuthRole.RestaurantAdmin
            ? AuthorizationScope.ForTenant(TenantId.From(tenant.TenantId))
            : AuthorizationScope.ForBranch(TenantId.From(tenant.TenantId), BranchId.From(membership.BranchId!.Value));

        var tokenResult = _jwtTokenGenerator.GenerateAccessToken(
            UserId.From(lookupUser.UserId),
            session.Id,
            PrincipalType.Staff,
            role,
            scope,
            AuthenticationMethod.Password,
            lookupUser.SecurityVersion,
            now);

        return new AuthResult(
            AccessToken: tokenResult.Token,
            RefreshToken: rawRefreshToken,
            AccessTokenExpiresAt: tokenResult.ExpiresAtUtc,
            RefreshTokenExpiresAt: refreshToken.ExpiresAtUtc,
            User: new UserPrincipalDto(lookupUser.UserId, lookupUser.NormalizedEmail, role.ToString(), tenant.TenantId, membership.BranchId, lookupUser.SecurityVersion),
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
            var pUser = await _bootstrapGateway.LookupUserByIdAsync(platformSession.UserId.Value, ct);
            if (pUser == null || pUser.Status != (int)UserStatus.Active) throw new AuthFailureException("User account is not active.");

            var pTokenResult = _jwtTokenGenerator.GenerateAccessToken(
                platformSession.UserId,
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
                User: new UserPrincipalDto(pUser.UserId, pUser.Email, AuthRole.SuperAdmin.ToString(), null, null, pUser.SecurityVersion),
                Session: new SessionDto(platformSession.SessionId, "Password", "Active", platformSession.CreatedAtUtc, now, platformSession.ExpiresAtUtc, IsCurrent: true));
        }

        if (isReuse) throw new AuthFailureException("Refresh token reuse detected.");

        var lookup = await _bootstrapGateway.LookupRefreshTokenForRotationAsync(tokenHash, ct);
        if (lookup == null)
        {
            throw new AuthFailureException("Invalid or expired refresh token.");
        }

        if (lookup.IsRevoked || lookup.ReplacedByTokenId.HasValue)
        {
            await _bootstrapGateway.HandleTenantTokenReuseAsync(lookup.TenantId, lookup.TokenFamilyId, lookup.SessionId, now, ct);
            throw new AuthFailureException("Refresh token reuse detected.");
        }

        if (lookup.ExpiresAtUtc <= now || lookup.SessionIsRevoked || lookup.SessionExpiresAtUtc <= now)
        {
            throw new AuthFailureException("Invalid or expired refresh token.");
        }

        var newTokenId = Guid.NewGuid();
        var rotated = await _bootstrapGateway.RotateTenantRefreshTokenAsync(
            lookup.TenantId,
            lookup.TokenId,
            newTokenId,
            lookup.SessionId,
            lookup.TokenFamilyId,
            newTokenHash,
            tokenLifetime,
            now,
            ct);

        if (!rotated)
        {
            await _bootstrapGateway.HandleTenantTokenReuseAsync(lookup.TenantId, lookup.TokenFamilyId, lookup.SessionId, now, ct);
            throw new AuthFailureException("Refresh token reuse detected.");
        }

        var user = await _bootstrapGateway.LookupUserByIdAsync(lookup.UserId, ct);
        if (user == null || user.Status != (int)UserStatus.Active)
        {
            throw new AuthFailureException("User is inactive.");
        }

        var membership = await _bootstrapGateway.LookupMembershipForLoginAsync(lookup.TenantId, lookup.UserId, ct);
        if (membership == null || !membership.IsActive)
        {
            throw new AuthFailureException("Membership is inactive.");
        }

        if (!Enum.TryParse<AuthRole>(membership.Role, out var role))
        {
            throw new AuthFailureException("Invalid membership role.");
        }

        var scope = role == AuthRole.RestaurantAdmin
            ? AuthorizationScope.ForTenant(TenantId.From(lookup.TenantId))
            : AuthorizationScope.ForBranch(TenantId.From(lookup.TenantId), BranchId.From(membership.BranchId!.Value));

        var authMethod = (AuthenticationMethod)lookup.AuthMethod;
        var tokenResult = _jwtTokenGenerator.GenerateAccessToken(
            UserId.From(user.UserId),
            lookup.SessionId,
            PrincipalType.Staff,
            role,
            scope,
            authMethod,
            user.SecurityVersion,
            now);

        return new AuthResult(
            AccessToken: tokenResult.Token,
            RefreshToken: newRawToken,
            AccessTokenExpiresAt: tokenResult.ExpiresAtUtc,
            RefreshTokenExpiresAt: now.Add(tokenLifetime),
            User: new UserPrincipalDto(user.UserId, user.Email, role.ToString(), lookup.TenantId, membership.BranchId, user.SecurityVersion),
            Session: new SessionDto(lookup.SessionId, authMethod.ToString(), "Active", now, now, lookup.SessionExpiresAtUtc, IsCurrent: true));
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
        var failedAttempts = 1;
        DateTimeOffset? lockoutEnd = null;
        if (failedAttempts >= _authSettings.MaxFailedLoginAttempts)
        {
            lockoutEnd = now.AddMinutes(_authSettings.LockoutMinutes);
        }

        await _bootstrapGateway.RecordFailedLoginAttemptAsync(userId, failedAttempts, lockoutEnd, now, ct);
    }
}
