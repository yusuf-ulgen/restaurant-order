namespace RestaurantOrder.Domain.Orders;

/// <summary>
/// Lifecycle status of an individual item within an Order.
/// </summary>
public enum OrderItemStatus
{
    Draft = 1,
    Submitted = 2,
    InPreparation = 3,
    Ready = 4,
    Served = 5,
    Cancelled = 6
}
