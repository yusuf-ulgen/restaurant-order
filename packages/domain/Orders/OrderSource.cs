namespace RestaurantOrder.Domain.Orders;

/// <summary>
/// Origin of the order placement.
/// Customer: Placed via mobile QR customer web app.
/// Staff: Placed or modified directly by waiter/cashier/staff app.
/// </summary>
public enum OrderSource
{
    Customer = 1,
    Staff = 2
}
