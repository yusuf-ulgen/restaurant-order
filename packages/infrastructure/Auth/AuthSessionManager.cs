using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Manages session persistence, active session listing, and session/token family revocations.
/// </summary>
public sealed class AuthSessionManager : IAuthSessionManager
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IPlatformSessionStore _platformSessionStore;

    public AuthSessionManager(
        RestaurantOrderDbContext dbContext,
        IPlatformSessionStore platformSessionStore)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _platformSessionStore = platformSessionStore ?? throw new ArgumentNullException(nameof(platformSessionStore));
    }

    public async Task LogoutAsync(Guid sessionId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _platformSessionStore.RevokeSessionAsync(sessionId, "user_logout", now);

        var session = await _dbContext.Sessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session != null)
        {
            session.Revoke("user_logout", now);
            var tokens = await _dbContext.RefreshTokens
                .IgnoreQueryFilters()
                .Where(rt => rt.SessionId == sessionId && !rt.IsRevoked)
                .ToListAsync(ct);

            foreach (var t in tokens)
            {
                t.Revoke(now);
            }

            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task LogoutAllAsync(UserId userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _platformSessionStore.RevokeAllUserSessionsAsync(userId, now);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        user?.IncrementSecurityVersion(now);

        var sessions = await _dbContext.Sessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .ToListAsync(ct);

        var sessionIds = sessions.Select(s => s.Id).ToList();
        foreach (var s in sessions)
        {
            s.Revoke("logout_all", now);
        }

        var tokens = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => sessionIds.Contains(rt.SessionId) && !rt.IsRevoked)
            .ToListAsync(ct);

        foreach (var t in tokens)
        {
            t.Revoke(now);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task RevokeSessionAsync(UserId currentUserId, Guid targetSessionId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _platformSessionStore.RevokeSessionAsync(targetSessionId, "manual_revocation", now);

        var session = await _dbContext.Sessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == targetSessionId, ct);

        if (session != null && session.UserId == currentUserId)
        {
            session.Revoke("manual_revocation", now);
            var tokens = await _dbContext.RefreshTokens
                .IgnoreQueryFilters()
                .Where(rt => rt.SessionId == targetSessionId && !rt.IsRevoked)
                .ToListAsync(ct);

            foreach (var t in tokens)
            {
                t.Revoke(now);
            }

            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<SessionDto>> GetActiveSessionsAsync(UserId userId, Guid? currentSessionId = null, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var platformSessions = await _platformSessionStore.GetActiveSessionsAsync(userId, now);
        if (platformSessions.Count > 0)
        {
            return platformSessions
                .Select(s => new SessionDto(s.SessionId, "Password", "Active", s.CreatedAtUtc, s.LastSeenAtUtc, s.ExpiresAtUtc, s.SessionId == currentSessionId))
                .ToList();
        }

        var dbSessions = await _dbContext.Sessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == userId && !s.IsRevoked && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.LastSeenAtUtc)
            .ToListAsync(ct);

        return dbSessions
            .Select(s => new SessionDto(s.Id, s.AuthMethod.ToString(), "Active", s.CreatedAtUtc, s.LastSeenAtUtc, s.ExpiresAtUtc, s.Id == currentSessionId))
            .ToList();
    }

    public async Task HandleTokenReuseAsync(RefreshToken token, DateTimeOffset now, CancellationToken ct = default)
    {
        var familyTokens = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.TokenFamilyId == token.TokenFamilyId && !rt.IsRevoked)
            .ToListAsync(ct);

        foreach (var ft in familyTokens)
        {
            ft.Revoke(now);
        }

        var session = await _dbContext.Sessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == token.SessionId, ct);

        session?.Revoke("Token reuse detected", now);

        var audit = SecurityAuditEvent.Create(
            token.TenantId,
            SecurityAuditEventType.RefreshTokenReuseDetected,
            now,
            detailsJson: $"{{\"familyId\":\"{token.TokenFamilyId}\"}}");
        _dbContext.SecurityAuditEvents.Add(audit);

        await _dbContext.SaveChangesAsync(ct);
    }
}
