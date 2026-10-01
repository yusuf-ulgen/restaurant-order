namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Verified authentication mechanism used to establish an identity session.
/// </summary>
public enum AuthenticationMethod
{
    /// <summary>
    /// Email and password authentication (administrative management).
    /// </summary>
    Password = 1,

    /// <summary>
    /// 4-digit staff PIN login operating strictly inside a trusted terminal context.
    /// </summary>
    Pin = 2,

    /// <summary>
    /// Anonymous QR scan dining session bound to a single table session.
    /// </summary>
    CustomerQrSession = 3
}
