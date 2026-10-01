using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Core authentication application service governing password login, session creation,
/// token rotation, and revocation lifecycles.
/// </summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginCommand command, CancellationToken ct = default);

    Task<AuthResult> RefreshSessionAsync(string rawRefreshToken, string? ipAddress, string? userAgent, CancellationToken ct = default);

    Task LogoutAsync(Guid sessionId, CancellationToken ct = default);

    Task LogoutAllAsync(UserId userId, CancellationToken ct = default);

    Task<bool> RevokeSessionAsync(UserId currentUserId, Guid targetSessionId, CancellationToken ct = default);

    Task<IReadOnlyList<SessionDto>> GetActiveSessionsAsync(UserId userId, Guid? currentSessionId = null, CancellationToken ct = default);
}
