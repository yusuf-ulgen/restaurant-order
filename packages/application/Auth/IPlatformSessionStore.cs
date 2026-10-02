using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Application.Auth;

public sealed record PlatformSessionInfo(
    Guid SessionId,
    UserId UserId,
    Guid FamilyId,
    string CurrentTokenHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool IsRevoked);

public interface IPlatformSessionStore
{
    Task CreateSessionAsync(
        UserId userId,
        Guid sessionId,
        Guid familyId,
        string tokenHash,
        TimeSpan sessionLifetime,
        DateTimeOffset nowUtc);

    Task<(bool Success, bool ReuseDetected, PlatformSessionInfo? Session)> RotateTokenAsync(
        string oldTokenHash,
        string newTokenHash,
        TimeSpan tokenLifetime,
        DateTimeOffset nowUtc);

    Task<bool> RevokeSessionAsync(Guid sessionId, string reason, DateTimeOffset nowUtc, UserId? userId = null);

    Task RevokeAllUserSessionsAsync(UserId userId, DateTimeOffset nowUtc);

    Task<IReadOnlyList<PlatformSessionInfo>> GetActiveSessionsAsync(UserId userId, DateTimeOffset nowUtc);

    Task<bool> IsSessionActiveAsync(Guid sessionId, DateTimeOffset nowUtc);
}
