namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Contract for validating that an access token's underlying session and security version
/// remain active and unrevoked before tenant context is established.
/// </summary>
public interface ITokenRevocationValidator
{
    Task<bool> ValidateTokenActiveAsync(
        Guid sessionId,
        Guid userId,
        int securityVersion,
        CancellationToken ct = default);

    Task InvalidateSessionCacheAsync(Guid sessionId, CancellationToken ct = default);
    Task InvalidateUserCacheAsync(Guid userId, CancellationToken ct = default);
}
