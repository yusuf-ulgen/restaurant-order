using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Service contract for staff 4-digit PIN operations on trusted terminals.
/// </summary>
public interface IStaffPinAuthService
{
    Task<AuthResult> LoginWithPinAsync(
        PinLoginCommand command,
        CancellationToken ct = default);

    Task SetPinAsync(
        TenantId tenantId,
        SetStaffPinCommand command,
        UserId actorUserId,
        CancellationToken ct = default);

    Task LogoutPinSessionAsync(
        Guid sessionId,
        Guid terminalId,
        CancellationToken ct = default);
}
