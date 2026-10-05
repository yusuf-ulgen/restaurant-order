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
    string PublicCode,
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

/// <summary>
/// Data transfer object representing generated static QR code for a table.
/// </summary>
public record TableQrCodeDto(
    Guid TableId,
    string PublicCode,
    int QrVersion,
    string Mode,
    string Token,
    string Svg);

/// <summary>
/// Data transfer object representing generated dynamic QR code for an active dining session.
/// </summary>
public record SessionDynamicQrDto(
    Guid SessionId,
    Guid TableId,
    string PublicCode,
    string Mode,
    string Token,
    string Svg,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Metadata for table QR code state (does not leak keys or secret tokens).
/// </summary>
public record TableQrMetadataDto(
    Guid TableId,
    string TableNumber,
    string Name,
    string PublicCode,
    int QrVersion,
    bool IsActive,
    string KeyId);

/// <summary>
/// Request payload to rotate table QR version.
/// </summary>
public record RotateQrVersionRequest(
    Guid? ConcurrencyToken = null);

/// <summary>
/// Public response returned when resolving a signed table QR token.
/// </summary>
public record QrResolveResponse(
    Guid TenantId,
    Guid BranchId,
    string BrandName,
    string BranchName,
    string TableNumber,
    string TableName,
    string Mode,
    bool HasActiveSession,
    string? ActiveSessionStatus);

/// <summary>
/// Request payload for exchanging a signed QR token for customer access credentials.
/// </summary>
public record QrExchangeRequest(
    string Token);

/// <summary>
/// Internal result produced when exchanging a signed QR token.
/// </summary>
public record QrExchangeResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    Guid SessionId,
    string SessionStatus,
    Guid TenantId,
    Guid BranchId,
    string TableNumber,
    string TableName);

