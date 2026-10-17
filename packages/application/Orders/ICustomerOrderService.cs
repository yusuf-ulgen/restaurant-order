using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Orders;

/// <summary>
/// Application service governing customer order lifecycle, idempotent submission,
/// and session-scoped order queries.
/// </summary>
public interface ICustomerOrderService
{
    /// <summary>
    /// Atomically validates, prices, and submits a customer order within an active dining session.
    /// Safely handles idempotency replays and prevents duplicate rounds via advisory locks.
    /// </summary>
    Task<CreateCustomerOrderResult> CreateOrderAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        CreateCustomerOrderRequest request,
        string idempotencyKey,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all orders placed within the customer's active dining session.
    /// </summary>
    Task<IReadOnlyList<CustomerOrderResponseDto>> GetOrdersAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific order by ID, strictly enforcing that it belongs to the customer's session.
    /// Returns null if not found or if session does not match.
    /// </summary>
    Task<CustomerOrderResponseDto?> GetOrderByIdAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        OrderId orderId,
        CancellationToken cancellationToken = default);
}
