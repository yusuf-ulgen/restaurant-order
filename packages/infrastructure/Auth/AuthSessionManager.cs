using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Manages session persistence, active session listing, and session/token family revocations.
/// Completely avoids IgnoreQueryFilters by operating through scoped tenant transactions
/// and least-privilege bootstrap gateways.
/// </summary>
public sealed class AuthSessionManager : IAuthSessionManager
{
    private readonly IPlatformSessionStore _platformSessionStore;
    private readonly IIamBootstrapGateway _bootstrapGateway;
    private readonly ITokenRevocationValidator? _tokenValidator;

    public AuthSessionManager(
        IPlatformSessionStore platformSessionStore,
        IIamBootstrapGateway bootstrapGateway,
        ITokenRevocationValidator? tokenValidator = null)
    {
        _platformSessionStore = platformSessionStore ?? throw new ArgumentNullException(nameof(platformSessionStore));
        _bootstrapGateway = bootstrapGateway ?? throw new ArgumentNullException(nameof(bootstrapGateway));
        _tokenValidator = tokenValidator;
    }

    public async Task LogoutAsync(Guid sessionId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _platformSessionStore.RevokeSessionAsync(sessionId, "user_logout", now);

        var sessionLookup = await _bootstrapGateway.LookupSessionTenantAsync(sessionId, ct);
        if (sessionLookup != null && sessionLookup.TenantId != Guid.Empty)
        {
            await _bootstrapGateway.RevokeTenantSessionAsync(sessionLookup.TenantId, sessionId, "user_logout", now, ct);
        }

        if (_tokenValidator != null)
        {
            await _tokenValidator.InvalidateSessionCacheAsync(sessionId, ct);
        }
    }

    public async Task LogoutAllAsync(UserId userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _platformSessionStore.RevokeAllUserSessionsAsync(userId, now);
        await _bootstrapGateway.RevokeAllUserSessionsAsync(userId.Value, now, ct);

        if (_tokenValidator != null)
        {
            await _tokenValidator.InvalidateUserCacheAsync(userId.Value, ct);
        }
    }

    public async Task<bool> RevokeSessionAsync(UserId currentUserId, Guid targetSessionId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var platformRevoked = await _platformSessionStore.RevokeSessionAsync(targetSessionId, "manual_revocation", now, currentUserId);
        if (platformRevoked)
        {
            if (_tokenValidator != null)
            {
                await _tokenValidator.InvalidateSessionCacheAsync(targetSessionId, ct);
            }
            return true;
        }

        var sessionLookup = await _bootstrapGateway.LookupSessionTenantAsync(targetSessionId, currentUserId.Value, ct);
        if (sessionLookup != null && sessionLookup.TenantId != Guid.Empty)
        {
            var tenantRevoked = await _bootstrapGateway.RevokeTenantSessionAsync(sessionLookup.TenantId, targetSessionId, "manual_revocation", now, currentUserId.Value, ct);
            if (tenantRevoked)
            {
                if (_tokenValidator != null)
                {
                    await _tokenValidator.InvalidateSessionCacheAsync(targetSessionId, ct);
                }
                return true;
            }
        }

        return false;
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

        var tenantSessions = await _bootstrapGateway.LookupActiveUserSessionsAsync(userId.Value, now, ct);
        return tenantSessions
            .Select(s => new SessionDto(s.SessionId, s.AuthMethod, "Active", s.CreatedAtUtc, s.LastSeenAtUtc, s.ExpiresAtUtc, s.SessionId == currentSessionId))
            .ToList();
    }

    public async Task HandleTokenReuseAsync(RefreshToken token, DateTimeOffset now, CancellationToken ct = default)
    {
        await _bootstrapGateway.HandleTenantTokenReuseAsync(
            token.TenantId.Value,
            token.TokenFamilyId,
            token.SessionId,
            now,
            ct);

        if (_tokenValidator != null)
        {
            await _tokenValidator.InvalidateSessionCacheAsync(token.SessionId, ct);
        }
    }
}
