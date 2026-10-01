namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Machine-readable security audit event type constants.
/// </summary>
public static class SecurityAuditEventType
{
    public const string LoginSucceeded = "login_succeeded";
    public const string LoginFailed = "login_failed";
    public const string SessionRefreshed = "session_refreshed";
    public const string SessionRevoked = "session_revoked";
    public const string RefreshTokenReuseDetected = "refresh_token_reuse_detected";
    public const string AccountLocked = "account_locked";
    public const string AccountUnlocked = "account_unlocked";
    public const string PasswordChanged = "password_changed";
    public const string PasswordReset = "password_reset";
    public const string RoleAssigned = "role_assigned";
    public const string RoleRemoved = "role_removed";
    public const string PinChanged = "pin_changed";
    public const string PinFailed = "pin_failed";
    public const string PinLocked = "pin_locked";
    public const string TrustedTerminalEnrolled = "trusted_terminal_enrolled";
    public const string TrustedTerminalRevoked = "trusted_terminal_revoked";
}
