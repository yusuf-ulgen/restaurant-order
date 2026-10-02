namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Minimal authentication projection returned by least-privilege login lookup.
/// Excludes all PII and sensitive user metadata.
/// </summary>
public sealed record UserLoginLookupDto(
    Guid UserId,
    string NormalizedEmail,
    string PasswordHash,
    int Status,
    int SecurityVersion,
    DateTimeOffset? LockoutEndUtc);
