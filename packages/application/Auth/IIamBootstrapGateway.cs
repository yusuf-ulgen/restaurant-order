namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Gateway contract for pre-authentication bootstrap lookups and least-privilege identity updates
/// adhering to ADR-0010. Operates securely before ambient tenant context exists.
/// </summary>
public interface IIamBootstrapGateway
{
    /// <summary>
    /// Looks up minimal tenant metadata by slug using dedicated SECURITY DEFINER function.
    /// </summary>
    Task<TenantLookupDto?> LookupTenantBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Resolves an active membership for a user in a tenant before tenant context is bound.
    /// </summary>
    Task<MembershipLookupDto?> LookupMembershipForLoginAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Looks up minimal invitation token metadata by token SHA-256 hash.
    /// </summary>
    Task<InvitationTokenLookupDto?> LookupInvitationTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Looks up minimal password reset token metadata by token SHA-256 hash.
    /// </summary>
    Task<PasswordResetTokenLookupDto?> LookupPasswordResetTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Looks up trusted terminal metadata for PIN authentication by terminal ID.
    /// </summary>
    Task<TerminalAuthLookupDto?> LookupTerminalForAuthAsync(Guid terminalId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the first active membership for a user across tenants (for self-service flows).
    /// </summary>
    Task<UserActiveMembershipSummaryDto?> LookupFirstActiveMembershipByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Looks up minimal user summary projection (excluding password hash) by user ID.
    /// </summary>
    Task<UserSummaryLookupDto?> LookupUserByIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Looks up session tenant ID by session ID before tenant context is set.
    /// </summary>
    Task<SessionTenantLookupDto?> LookupSessionTenantAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Updates user login timestamp, resets failed attempt count, and optionally rehashes password.
    /// Executes without requiring SELECT on iam.users.
    /// </summary>
    Task RecordSuccessfulLoginAsync(Guid userId, DateTimeOffset nowUtc, string? newPasswordHash = null, CancellationToken ct = default);

    /// <summary>
    /// Records failed login attempt and potential lockout on user without requiring SELECT on iam.users.
    /// </summary>
    Task RecordFailedLoginAttemptAsync(Guid userId, int failedAttempts, DateTimeOffset? lockoutEndUtc, DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Activates user and sets new password hash during invitation accept flow.
    /// </summary>
    Task ActivateUserAndSetPasswordAsync(Guid userId, string passwordHash, DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Updates user password hash and increments security version during self-service password reset flow.
    /// </summary>
    Task ResetUserPasswordAsync(Guid userId, string passwordHash, DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Updates user status and security version without requiring SELECT on iam.users.
    /// </summary>
    Task UpdateUserStatusAsync(Guid userId, RestaurantOrder.Domain.Auth.UserStatus status, DateTimeOffset nowUtc, CancellationToken ct = default);
}
