namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Data transfer object representing a physical restaurant dining table.
/// </summary>
public record RestaurantTableDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid DiningAreaId,
    string TableNumber,
    string Name,
    int Capacity,
    int PositionX,
    int PositionY,
    int Width,
    int Height,
    int RotationDegrees,
    string Shape,
    bool IsActive,
    int QrVersion,
    Guid ConcurrencyToken,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>
/// Request payload to create a new dining table in a branch.
/// </summary>
public record CreateTableRequest(
    Guid DiningAreaId,
    string TableNumber,
    string Name,
    int Capacity,
    int PositionX = 0,
    int PositionY = 0,
    int Width = 100,
    int Height = 100,
    int RotationDegrees = 0,
    string Shape = "Square",
    Guid? BranchId = null);

/// <summary>
/// Request payload to update metadata of an existing table.
/// </summary>
public record UpdateTableRequest(
    Guid DiningAreaId,
    string TableNumber,
    string Name,
    int Capacity,
    Guid? ConcurrencyToken = null);

/// <summary>
/// Request payload to update spatial canvas coordinates and layout of an existing table.
/// </summary>
public record UpdateTableLayoutRequest(
    int PositionX,
    int PositionY,
    int Width,
    int Height,
    int RotationDegrees,
    string Shape,
    Guid? ConcurrencyToken = null);
