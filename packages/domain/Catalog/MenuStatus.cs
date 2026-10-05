namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Lifecycle state of a restaurant menu.
/// </summary>
public enum MenuStatus
{
    /// <summary>
    /// Initial unpublished draft state where items and categories are being prepared.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// Active menu available for guest ordering and waiter operations.
    /// </summary>
    Active = 2,

    /// <summary>
    /// Terminal archived state. Cannot be reactivated or edited; preserved for historical records and audit logs.
    /// </summary>
    Archived = 3
}
