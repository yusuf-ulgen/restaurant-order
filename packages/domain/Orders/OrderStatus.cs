namespace RestaurantOrder.Domain.Orders;

/// <summary>
/// Lifecycle statuses for an Order aggregate.
/// Distinct from DiningSession statuses and Bill/Payment statuses.
/// </summary>
public enum OrderStatus
{
    Draft = 1,
    Submitted = 2,
    InPreparation = 3,
    Ready = 4,
    Served = 5,
    Cancelled = 6
}
