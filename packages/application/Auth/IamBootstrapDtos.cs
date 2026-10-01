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

