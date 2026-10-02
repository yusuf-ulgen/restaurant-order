using System.Collections.Concurrent;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Thread-safe in-memory store for platform SuperAdmin sessions and token families.
/// Completely decoupled from tenant RLS database tables.
/// </summary>
public sealed class InMemoryPlatformSessionStore : IPlatformSessionStore
{
    private sealed class TokenNode
    {
        public string TokenHash { get; set; } = string.Empty;
        public Guid SessionId { get; set; }
        public Guid FamilyId { get; set; }
        public bool IsConsumed { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, PlatformSessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<string, TokenNode> _tokens = new(StringComparer.Ordinal);
    private readonly object _syncLock = new();

    public Task CreateSessionAsync(
        UserId userId,
        Guid sessionId,
        Guid familyId,
        string tokenHash,
        TimeSpan sessionLifetime,
        DateTimeOffset nowUtc)
    {
        lock (_syncLock)
        {
            var session = new PlatformSessionInfo(
                SessionId: sessionId,
                UserId: userId,
                FamilyId: familyId,
                CurrentTokenHash: tokenHash,
                CreatedAtUtc: nowUtc,
                LastSeenAtUtc: nowUtc,
                ExpiresAtUtc: nowUtc.Add(sessionLifetime),
                IsRevoked: false);

            _sessions[sessionId] = session;

            _tokens[tokenHash] = new TokenNode
            {
                TokenHash = tokenHash,
                SessionId = sessionId,
                FamilyId = familyId,
                IsConsumed = false,
                ExpiresAtUtc = nowUtc.Add(sessionLifetime)
            };
        }

        return Task.CompletedTask;
    }

    public Task<(bool Success, bool ReuseDetected, PlatformSessionInfo? Session)> RotateTokenAsync(
        string oldTokenHash,
        string newTokenHash,
        TimeSpan tokenLifetime,
        DateTimeOffset nowUtc)
    {
        lock (_syncLock)
        {
            if (!_tokens.TryGetValue(oldTokenHash, out var node))
            {
                return Task.FromResult<(bool, bool, PlatformSessionInfo?)>((false, false, null));
            }

            if (!_sessions.TryGetValue(node.SessionId, out var session) || session.IsRevoked || session.ExpiresAtUtc <= nowUtc)
            {
                return Task.FromResult<(bool, bool, PlatformSessionInfo?)>((false, false, null));
            }

            // Reuse detected: token already consumed!
            if (node.IsConsumed)
            {
                // Revoke session and all tokens in family
                _sessions[session.SessionId] = session with { IsRevoked = true };
                foreach (var kvp in _tokens.Where(t => t.Value.FamilyId == node.FamilyId))
                {
                    kvp.Value.IsConsumed = true;
                }
                return Task.FromResult<(bool, bool, PlatformSessionInfo?)>((false, true, session));
            }

            if (node.ExpiresAtUtc <= nowUtc)
            {
                return Task.FromResult<(bool, bool, PlatformSessionInfo?)>((false, false, null));
            }

            // Consume old token
            node.IsConsumed = true;

            // Register new token
            _tokens[newTokenHash] = new TokenNode
            {
                TokenHash = newTokenHash,
                SessionId = session.SessionId,
                FamilyId = node.FamilyId,
                IsConsumed = false,
                ExpiresAtUtc = nowUtc.Add(tokenLifetime)
            };

            var updatedSession = session with
            {
                CurrentTokenHash = newTokenHash,
                LastSeenAtUtc = nowUtc
            };
            _sessions[session.SessionId] = updatedSession;

            return Task.FromResult<(bool, bool, PlatformSessionInfo?)>((true, false, updatedSession));
        }
    }

    public Task<bool> RevokeSessionAsync(Guid sessionId, string reason, DateTimeOffset nowUtc, UserId? userId = null)
    {
        lock (_syncLock)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                if (userId.HasValue && session.UserId != userId.Value)
                {
                    return Task.FromResult(false);
                }

                _sessions[sessionId] = session with { IsRevoked = true };
                foreach (var kvp in _tokens.Where(t => t.Value.SessionId == sessionId))
                {
                    kvp.Value.IsConsumed = true;
                }
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    public Task RevokeAllUserSessionsAsync(UserId userId, DateTimeOffset nowUtc)
    {
        lock (_syncLock)
        {
            var userSessions = _sessions.Values.Where(s => s.UserId == userId).ToList();
            foreach (var session in userSessions)
            {
                _sessions[session.SessionId] = session with { IsRevoked = true };
                foreach (var kvp in _tokens.Where(t => t.Value.SessionId == session.SessionId))
                {
                    kvp.Value.IsConsumed = true;
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlatformSessionInfo>> GetActiveSessionsAsync(UserId userId, DateTimeOffset nowUtc)
    {
        var list = _sessions.Values
            .Where(s => s.UserId == userId && !s.IsRevoked && s.ExpiresAtUtc > nowUtc)
            .OrderByDescending(s => s.LastSeenAtUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<PlatformSessionInfo>>(list);
    }

    public Task<bool> IsSessionActiveAsync(Guid sessionId, DateTimeOffset nowUtc)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult(!session.IsRevoked && session.ExpiresAtUtc > nowUtc);
        }

        return Task.FromResult(false);
    }
}
