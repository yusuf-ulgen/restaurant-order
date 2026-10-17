using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Orders;

public interface IStaffOrderService
{
    Task<CreateStaffOrderResult> CreateStaffOrderAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        CreateStaffOrderRequest request,
        string idempotencyKey,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffOrderResponseDto>> GetSessionOrdersAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default);

    Task<StaffOrderResponseDto> GetOrderByIdAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        OrderId orderId,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default);

    Task<StaffOrderResponseDto> CancelOrderItemAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        OrderId orderId,
        OrderItemId itemId,
        CancelOrderItemRequest request,
        Guid? ifMatchConcurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default);
}
