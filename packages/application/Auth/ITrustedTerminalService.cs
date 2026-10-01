using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Service contract for trusted terminal enrollment, activation, authentication and revocation.
/// </summary>
public interface ITrustedTerminalService
{
    Task<EnrollTerminalResult> CreateEnrollmentCodeAsync(
        TenantId tenantId,
        EnrollTerminalCommand command,
        UserId actorUserId,
        CancellationToken ct = default);

    Task<ActivateTerminalResult> ActivateTerminalAsync(
        ActivateTerminalCommand command,
        CancellationToken ct = default);

    Task<TerminalContext?> AuthenticateTerminalAsync(
        Guid terminalId,
        string deviceSecret,
        CancellationToken ct = default);

    Task<bool> RevokeTerminalAsync(
        TenantId tenantId,
        Guid terminalId,
        UserId actorUserId,
        CancellationToken ct = default);

    Task<TerminalContext?> GetCurrentTerminalAsync(
        Guid terminalId,
        string deviceSecret,
        CancellationToken ct = default);
}
