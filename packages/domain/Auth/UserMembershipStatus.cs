namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Lifecycle status of a user's membership within a specific tenant and branch.
/// Completely decoupled from global platform user status.
/// </summary>
public enum UserMembershipStatus
{
    /// <summary>Fully active and eligible for staff operations and PIN login.</summary>
    Active = 1,

    /// <summary>Temporarily suspended in this tenant/branch. Prohibits login and PIN authentication.</summary>
    Suspended = 2,

    /// <summary>Permanently disabled/deactivated in this tenant/branch.</summary>
    Disabled = 3
}
