namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Operational mode for signed table QR codes.
/// Static: Long-lived table identifier, points to table and dynamically joins/creates dining session.
/// Dynamic: Short-lived QR payload cryptographically bound to a specific active dining session.
/// </summary>
public enum QrMode
{
    Static = 1,
    Dynamic = 2
}
