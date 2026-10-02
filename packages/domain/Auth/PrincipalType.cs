namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// High-level principal classification distinguishing staff users from anonymous customer sessions.
/// </summary>
public enum PrincipalType
{
    /// <summary>
    /// Authenticated personnel operating within platform, tenant, or branch administrative/service boundaries.
    /// </summary>
    Staff = 1,

    /// <summary>
    /// Ephemeral dining guest bound exclusively to an active table session. Never a staff membership.
    /// </summary>
    Customer = 2
}
