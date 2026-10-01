namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Result outcome of staff PIN verification.
/// </summary>
public enum PinVerificationResult
{
    /// <summary>Verification failed; PIN does not match, format invalid, or pepper mismatched.</summary>
    Failed = 0,

    /// <summary>Verification succeeded with current algorithm parameters.</summary>
    Success = 1,

    /// <summary>Verification succeeded, but pepper key ID or algorithm should be re-hashed.</summary>
    SuccessRehashNeeded = 2
}
