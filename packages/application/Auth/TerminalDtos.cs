namespace RestaurantOrder.Application.Auth;

public sealed record EnrollTerminalCommand(
    Guid BranchId,
    string TerminalName,
    string DeviceIdentifier);

public sealed record EnrollTerminalResult(
    string EnrollmentCode,
    DateTimeOffset ExpiresAtUtc,
    Guid BranchId,
    string TerminalName);

public sealed record ActivateTerminalCommand(
    string EnrollmentCode,
    string DeviceIdentifier,
    string TerminalName);

public sealed record ActivateTerminalResult(
    Guid TerminalId,
    string DeviceSecret,
    Guid TenantId,
    Guid BranchId,
    string TerminalName);

public sealed record TerminalContext(
    Guid TerminalId,
    Guid TenantId,
    Guid BranchId,
    string TerminalName,
    string DeviceIdentifier,
    bool IsActive);

public sealed record PinLoginCommand(
    Guid TerminalId,
    string DeviceSecret,
    Guid? UserId,
    string? Email,
    string Pin,
    string? IpAddress = null,
    string? UserAgent = null);

public sealed record SetStaffPinCommand(
    Guid TargetUserId,
    Guid BranchId,
    string Pin);

/// <summary>
/// Thrown when staff PIN or terminal authentication fails.
/// Provides a generic non-enumerating message to clients to avoid user/terminal harvesting.
/// </summary>
public sealed class PinAuthFailureException : Exception
{
    public const string GenericMessage = "Invalid staff credentials or terminal access.";

    public PinAuthFailureException(string? internalReason = null)
        : base(GenericMessage)
    {
        InternalReason = internalReason ?? GenericMessage;
    }

    /// <summary>
    /// Diagnostic internal reason safe for audit trails, never returned to client.
    /// </summary>
    public string InternalReason { get; }
}
