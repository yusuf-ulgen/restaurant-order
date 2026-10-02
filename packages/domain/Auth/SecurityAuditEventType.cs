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

    // Restaurant Configuration
    public const string BrandCreated = "brand_created";
    public const string BrandUpdated = "brand_updated";
    public const string BrandActivated = "brand_activated";
    public const string BrandDeactivated = "brand_deactivated";
    public const string BranchCreated = "branch_created";
    public const string BranchUpdated = "branch_updated";
    public const string BranchActivated = "branch_activated";
    public const string BranchSuspended = "branch_suspended";
    public const string BranchClosed = "branch_closed";
    public const string BrandThemeUpdated = "brand_theme_updated";
    public const string BranchThemeOverrideUpdated = "branch_theme_override_updated";
    public const string BranchThemeOverrideCleared = "branch_theme_override_cleared";
}
