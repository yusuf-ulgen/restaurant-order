using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Orders;

/// <summary>
/// Application service boundary for order and item lifecycle transitions,
/// prepared for Phase 11 (KDS Bump / Recall / Delivery).
/// </summary>
public interface IOrderLifecycleService
{
    Task<OrderLifecycleResult> StartPreparationAsync(
        TenantId tenantId,
        BranchId branchId,
        OrderId orderId,
        OrderItemId? itemId,
        Guid actorId,
        string actorRole,
        Guid concurrencyToken,
        CancellationToken cancellationToken = default);

    Task<OrderLifecycleResult> MarkReadyAsync(
        TenantId tenantId,
        BranchId branchId,
        OrderId orderId,
        OrderItemId? itemId,
        Guid actorId,
        string actorRole,
        Guid concurrencyToken,
        CancellationToken cancellationToken = default);

    Task<OrderLifecycleResult> RecallToPreparationAsync(
        TenantId tenantId,
        BranchId branchId,
        OrderId orderId,
        OrderItemId? itemId,
        Guid actorId,
        string actorRole,
        Guid concurrencyToken,
        CancellationToken cancellationToken = default);

    Task<OrderLifecycleResult> MarkServedAsync(
        TenantId tenantId,
        BranchId branchId,
        OrderId orderId,
        OrderItemId? itemId,
        Guid actorId,
        string actorRole,
        Guid concurrencyToken,
        CancellationToken cancellationToken = default);
}
