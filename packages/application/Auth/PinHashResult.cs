namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Encapsulates the output of a salted, peppered PIN hash operation.
/// </summary>
public sealed record PinHashResult(
    string Hash,
    string AlgorithmVersion,
    string PepperKeyId);
