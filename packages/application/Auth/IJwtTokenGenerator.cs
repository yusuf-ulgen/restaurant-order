using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Contract for creating cryptographically signed JWT access tokens conforming to ADR-0009 and Phase 3.1 claim contracts.
/// </summary>
public interface IJwtTokenGenerator
{
    JwtTokenResult GenerateAccessToken(
        UserId userId,
        Guid sessionId,
        PrincipalType principalType,
        AuthRole role,
        AuthorizationScope scope,
        AuthenticationMethod authMethod,
        int securityVersion,
        DateTimeOffset nowUtc);
}

public sealed record JwtTokenResult(
    string Token,
    DateTimeOffset ExpiresAtUtc,
    string Jti);
