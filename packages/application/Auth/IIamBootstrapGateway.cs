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

    /// <summary>
    /// Pre-rotation lookup for tenant refresh token metadata by token hash.
    /// </summary>
    Task<RefreshTokenRotationLookupDto?> LookupRefreshTokenForRotationAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Atomically rotates a tenant refresh token under an active tenant transaction.
    /// Returns true if old token was active and replaced; returns false if token was already consumed (reuse race).
    /// </summary>
    Task<bool> RotateTenantRefreshTokenAsync(
        Guid tenantId,
        Guid oldTokenId,
        Guid newTokenId,
        Guid sessionId,
        Guid familyId,
        string newTokenHash,
        TimeSpan tokenLifetime,
        DateTimeOffset nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes an entire tenant token family and session upon token reuse detection under tenant transaction.
    /// </summary>
    Task HandleTenantTokenReuseAsync(
        Guid tenantId,
        Guid familyId,
        Guid sessionId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Increments user security version to immediately invalidate all distributed active tokens.
    /// </summary>
    Task IncrementSecurityVersionAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Revokes a tenant session and all its active refresh tokens under tenant transaction.
    /// </summary>
    Task RevokeTenantSessionAsync(
        Guid tenantId,
        Guid sessionId,
        string reason,
        DateTimeOffset nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes all active tenant sessions and refresh tokens for a user across tenants.
    /// </summary>
    Task RevokeAllUserSessionsAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Looks up active tenant sessions for a user without requiring blanket tenant context.
    /// </summary>
    Task<IReadOnlyList<UserSessionSummaryDto>> LookupActiveUserSessionsAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken ct = default);
}

