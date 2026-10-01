namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Projection returned by pre-authentication tenant lookup by slug.
/// </summary>
public record TenantLookupDto(
    Guid TenantId,
    string Name,
    string Slug,
    int Status);

/// <summary>
/// Minimal projection returned by pre-authentication membership resolution.
/// </summary>
public record MembershipLookupDto(
    Guid MembershipId,
    Guid TenantId,
    Guid UserId,
    string Role,
    Guid? BranchId,
    bool IsActive);

/// <summary>
/// Minimal projection returned by pre-authentication invitation token lookup.
/// </summary>
public record InvitationTokenLookupDto(
    Guid InvitationId,
    Guid TenantId,
    Guid UserId,
    DateTimeOffset ExpiresAtUtc,
    bool IsConsumed);

/// <summary>
/// Minimal projection returned by pre-authentication password reset token lookup.
/// </summary>
public record PasswordResetTokenLookupDto(
    Guid ResetTokenId,
    Guid TenantId,
    Guid UserId,
    DateTimeOffset ExpiresAtUtc,
    bool IsConsumed);

/// <summary>
/// Minimal projection returned by pre-authentication trusted terminal lookup for PIN authentication.
/// </summary>
public record TerminalAuthLookupDto(
    Guid TerminalId,
    Guid TenantId,
    Guid BranchId,
    string DeviceIdentifier,
    string TerminalName,
    string SecretHash,
    bool IsActive);

/// <summary>
/// Minimal projection for resolving active membership and tenant by user ID.
/// </summary>
public record UserActiveMembershipSummaryDto(
    Guid MembershipId,
    Guid TenantId,
    Guid? BranchId,
    string Role);

/// <summary>
/// Minimal projection for user identity without password hash.
/// </summary>
public record UserSummaryLookupDto(
    Guid UserId,
    string Email,
    string NormalizedEmail,
    int Status,
    int SecurityVersion,
    DateTimeOffset? LockoutEndUtc);

/// <summary>
/// Minimal projection for mapping session ID to tenant ID.
/// </summary>
public record SessionTenantLookupDto(
    Guid SessionId,
    Guid TenantId);

/// <summary>
/// Minimal projection returned by pre-rotation tenant refresh token lookup.
/// </summary>
public record RefreshTokenRotationLookupDto(
    Guid TokenId,
    Guid TenantId,
    Guid SessionId,
    Guid TokenFamilyId,
    DateTimeOffset ExpiresAtUtc,
    bool IsRevoked,
    Guid? ReplacedByTokenId,
    Guid UserId,
    int AuthMethod,
    bool SessionIsRevoked,
    DateTimeOffset SessionExpiresAtUtc);

/// <summary>
/// Minimal projection for active user session listing.
/// </summary>
public record UserSessionSummaryDto(
    Guid SessionId,
    Guid TenantId,
    string AuthMethod,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc);
/// <summary>
/// Result of atomic single-statement invitation token consumption.
/// </summary>
public record ConsumedInvitationTokenDto(
    Guid InvitationId,
    Guid TenantId,
    Guid UserId);

/// <summary>
/// Result of atomic single-statement password reset token consumption.
/// </summary>
public record ConsumedPasswordResetTokenDto(
    Guid ResetTokenId,
    Guid TenantId,
    Guid UserId);
