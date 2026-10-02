namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Gateway contract for least-privilege login lookup adhering to ADR-0010.
/// Avoids granting wide SELECT privileges over iam.users to the application runtime database role.
/// </summary>
public interface IIamUserLookupGateway
{
    /// <summary>
    /// Looks up minimal authentication credentials for a user by normalized email.
    /// Returns null if user does not exist.
    /// </summary>
    Task<UserLoginLookupDto?> LookupUserForLoginAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);
}
