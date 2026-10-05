namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Lifecycle status of a dining session at a restaurant table.
/// State machine follows strict linear progression: Open -> Active -> BillRequested -> Closed.
/// </summary>
public enum DiningSessionStatus
{
    Open = 1,
    Active = 2,
    BillRequested = 3,
    Closed = 4
}
