using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Service interface governing staff operations on customer service requests (list, acknowledge, complete).
/// Enforces fail-closed multi-tenant RBAC (service.request.manage), optimistic concurrency via ConcurrencyToken,
/// and transactional outbox event dispatch.
/// </summary>
public interface IStaffServiceRequestService
{
    Task<IReadOnlyList<StaffServiceRequestDto>> ListServiceRequestsAsync(
        TenantId tenantId,
        BranchId branchId,
        ServiceRequestStatus? statusFilter,
        RestaurantTableId? tableFilter,
        DiningSessionId? sessionFilter,
        int limit,
        int offset,
        AuthenticatedPrincipal actor,
        bool activeOnly = false,
        CancellationToken ct = default);

    Task<StaffServiceRequestDto> AcknowledgeRequestAsync(
        TenantId tenantId,
        BranchId branchId,
        ServiceRequestId requestId,
        Guid concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<StaffServiceRequestDto> CompleteRequestAsync(
        TenantId tenantId,
        BranchId branchId,
        ServiceRequestId requestId,
        Guid concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);
}
