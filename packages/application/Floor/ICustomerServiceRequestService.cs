using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Service contract for customer-initiated floor service requests.
/// Enforces tenant isolation, own-session scope, session lifecycle validity,
/// idempotency, and partial-unique open request constraints.
/// </summary>
public interface ICustomerServiceRequestService
{
    Task<ServiceRequestDto> CreateRequestAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        CreateCustomerServiceRequest request,
        string idempotencyKey,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<IReadOnlyList<ServiceRequestDto>> GetOpenRequestsAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<ServiceRequestDto> CancelRequestAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        ServiceRequestId requestId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);
}
