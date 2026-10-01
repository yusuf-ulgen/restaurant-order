namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Result outcome of password verification.
/// </summary>
public enum PasswordVerificationResult
{
    /// <summary>Verification failed; password does not match or hash is invalid.</summary>
    Failed = 0,

    /// <summary>Verification succeeded with current algorithm parameters.</summary>
    Success = 1,

    /// <summary>Verification succeeded, but algorithm parameters or format should be re-hashed.</summary>
    SuccessRehashNeeded = 2
}
