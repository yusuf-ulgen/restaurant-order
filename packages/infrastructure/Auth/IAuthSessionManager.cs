using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

public interface IAuthSessionManager
{
    Task LogoutAsync(Guid sessionId, CancellationToken ct = default);

    Task LogoutAllAsync(UserId userId, CancellationToken ct = default);

    Task RevokeSessionAsync(UserId currentUserId, Guid targetSessionId, CancellationToken ct = default);

    Task<IReadOnlyList<SessionDto>> GetActiveSessionsAsync(UserId userId, Guid? currentSessionId = null, CancellationToken ct = default);

    Task HandleTokenReuseAsync(RefreshToken token, DateTimeOffset now, CancellationToken ct = default);
}
