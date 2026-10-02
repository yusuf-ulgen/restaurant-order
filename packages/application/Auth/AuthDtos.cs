using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Application.Auth;

public sealed record LoginCommand(
    string Email,
    string Password,
    string? TenantSlug = null,
    string? IpAddress = null,
    string? UserAgent = null);

public sealed record AuthResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    UserPrincipalDto User,
    SessionDto Session);

public sealed record UserPrincipalDto(
    Guid UserId,
    string Email,
    string Role,
    Guid? TenantId,
    Guid? BranchId,
    int SecurityVersion);

public sealed record SessionDto(
    Guid Id,
    string AuthMethod,
    string SessionState,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastActivityUtc,
    DateTimeOffset ExpiresAtUtc,
    bool IsCurrent);

/// <summary>
/// Thrown when authentication fails for any reason (unknown email, invalid password, locked, etc.)
/// Provides a generic, non-enumerating error message to clients.
/// </summary>
public sealed class AuthFailureException : Exception
{
    public const string GenericMessage = "Invalid email or password.";

    public AuthFailureException(string? internalReason = null)
        : base(GenericMessage)
    {
        InternalReason = internalReason ?? GenericMessage;
    }

    /// <summary>
    /// Detailed diagnostic reason safe for internal logging and structured audit events, never exposed to clients.
    /// </summary>
    public string InternalReason { get; }
}
