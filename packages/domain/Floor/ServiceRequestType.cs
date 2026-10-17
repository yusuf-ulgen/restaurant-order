namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Supported customer service request types.
/// Note: Bill request is governed separately by the DiningSession aggregate lifecycle.
/// </summary>
public enum ServiceRequestType
{
    Waiter = 1,
    Water = 2,
    Napkin = 3,
    Assistance = 4
}
