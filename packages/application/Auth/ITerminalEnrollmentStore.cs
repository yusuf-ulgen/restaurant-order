namespace RestaurantOrder.Application.Auth;

public sealed record TerminalEnrollmentTicket(
    string CodeHash,
    Guid TenantId,
    Guid BranchId,
    string TerminalName,
    string DeviceIdentifier,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Ephemeral storage contract for terminal enrollment codes.
/// Enforces single-use consumption and automatic expiration.
/// </summary>
public interface ITerminalEnrollmentStore
{
    Task StoreEnrollmentTicketAsync(
        string codeHash,
        TerminalEnrollmentTicket ticket,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<TerminalEnrollmentTicket?> ConsumeEnrollmentTicketAsync(
        string codeHash,
        CancellationToken ct = default);
}
