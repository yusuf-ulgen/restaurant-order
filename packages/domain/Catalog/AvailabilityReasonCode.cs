namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Closed enumeration of valid business reasons for taking an item or variant out of service (86'd) or restocking.
/// </summary>
public enum AvailabilityReasonCode
{
    SoldOut = 1,
    IngredientUnavailable = 2,
    TemporarilyDisabled = 3,
    KitchenCapacity = 4,
    Manual = 5,
    Restocked = 6
}
