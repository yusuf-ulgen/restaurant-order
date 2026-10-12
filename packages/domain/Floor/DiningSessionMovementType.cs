namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Discriminator representing the type of table/session movement in the dining room.
/// </summary>
public enum DiningSessionMovementType
{
    Transfer = 1,
    Merge = 2
}
