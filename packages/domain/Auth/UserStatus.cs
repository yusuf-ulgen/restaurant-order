namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Lifecycle status of a platform user account.
/// </summary>
public enum UserStatus
{
    /// <summary>Invited or registered, pending initial activation/verification.</summary>
    Pending = 1,

    /// <summary>Fully active and eligible for authentication.</summary>
    Active = 2,

    /// <summary>Temporarily locked due to repeated failed credential attempts.</summary>
    Locked = 3,

    /// <summary>Suspended by an administrator. Login prohibited until resumed.</summary>
    Suspended = 4,

    /// <summary>Permanently disabled/deactivated.</summary>
    Disabled = 5
}
