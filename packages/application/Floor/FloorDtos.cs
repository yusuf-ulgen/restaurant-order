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

/// <summary>
/// Data transfer object representing a dining session at a table.
/// </summary>
public record DiningSessionDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid TableId,
    string Status,
    int GuestCount,
    Guid? AssignedWaiterId,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? BillRequestedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    string? CloseReason,
    Guid? MergedIntoSessionId,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>
/// Composite DTO combining table layout details and its current active session (if any).
/// </summary>
public record TableFloorStatusDto(
    RestaurantTableDto Table,
    DiningSessionDto? ActiveSession);

/// <summary>
/// Composite DTO representing live floor occupancy status for a whole branch.
/// </summary>
public record BranchFloorStatusDto(
    Guid BranchId,
    IReadOnlyList<TableFloorStatusDto> Tables);

/// <summary>
/// Request payload to open a new dining session at a table.
/// </summary>
public record OpenDiningSessionRequest(
    int GuestCount,
    Guid? AssignedWaiterId = null);

/// <summary>
/// Request payload to transition session to Closed.
/// </summary>
public record CloseDiningSessionRequest(
    string? Reason = null,
    Guid? ConcurrencyToken = null);

/// <summary>
/// Request payload for session state transitions (e.g. Activate, RequestBill).
/// </summary>
public record TransitionSessionRequest(
    Guid? ConcurrencyToken = null);
