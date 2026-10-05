namespace RestaurantOrder.Api.Floor;

public sealed record CreateTableApiRequest(
    Guid DiningAreaId,
    string TableNumber,
    string DisplayName,
    int Capacity,
    int PositionX = 0,
    int PositionY = 0,
    int Width = 100,
    int Height = 100,
    int RotationDegrees = 0,
    string Shape = "Square",
    Guid? BranchId = null);

public sealed record UpdateTableApiRequest(
    Guid DiningAreaId,
    string TableNumber,
    string DisplayName,
    int Capacity,
    Guid? ConcurrencyToken = null,
    Guid? BranchId = null);

public sealed record UpdateTableLayoutApiRequest(
    int PositionX,
    int PositionY,
    int Width,
    int Height,
    int RotationDegrees,
    string Shape = "Square",
    Guid? ConcurrencyToken = null);

public sealed record TableStateApiRequest(Guid? ConcurrencyToken = null);
