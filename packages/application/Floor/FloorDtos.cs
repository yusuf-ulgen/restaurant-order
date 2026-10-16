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
/// Individual table layout item for atomic batch update.
/// </summary>
public record TableLayoutBatchItem(
    Guid TableId,
    int PositionX,
    int PositionY,
    int Width,
    int Height,
    int RotationDegrees,
    string Shape,
    Guid? ConcurrencyToken = null);

/// <summary>
/// Request payload to atomically update spatial canvas coordinates for multiple tables in a branch.
/// </summary>
public record BatchUpdateTableLayoutRequest(
    IReadOnlyList<TableLayoutBatchItem> Items);

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

/// <summary>
/// Safe customer session metadata returned by GET /api/v1/customer/session.
/// Does not leak access tokens, credentials, or cross-tenant internals.
/// </summary>
public record CustomerSessionDto(
    Guid SessionId,
    Guid TenantId,
    Guid BranchId,
    Guid TableId,
    string TableNumber,
    string TableName,
    string SessionStatus,
    Guid ConcurrencyToken,
    int GuestCount,
    DateTimeOffset OpenedAtUtc);

/// <summary>
/// Request payload to transfer a dining session to another active table.
/// TargetTableConcurrencyToken is optional because deterministic row-level locks
/// (SELECT ... FOR UPDATE in ascending UUID order) provide transactional isolation.
/// IdempotencyKey is optional in body when provided via the 'Idempotency-Key' HTTP header.
/// </summary>
public record TransferSessionRequest(
    Guid TargetTableId,
    Guid? ConcurrencyToken = null,
    Guid? TargetTableConcurrencyToken = null,
    string? IdempotencyKey = null,
    string? Reason = null);

/// <summary>
/// Request payload to merge a source dining session into a target session.
/// SourceConcurrencyToken can be provided via body or If-Match header.
/// TargetConcurrencyToken is required to prevent stale session merges.
/// IdempotencyKey is optional in body when provided via the 'Idempotency-Key' HTTP header.
/// </summary>
public record MergeSessionRequest(
    Guid TargetSessionId,
    Guid? SourceConcurrencyToken = null,
    Guid? TargetConcurrencyToken = null,
    string? IdempotencyKey = null,
    string? Reason = null);

/// <summary>
/// DTO representing an immutable dining session table transfer or session merge audit event.
/// </summary>
public record DiningSessionMovementDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid SourceSessionId,
    Guid SourceTableId,
    Guid? TargetSessionId,
    Guid TargetTableId,
    string MovementType,
    Guid ActorId,
    string ActorRole,
    string? Reason,
    string IdempotencyKey,
    int GuestsMoved,
    DateTimeOffset CreatedAtUtc);
