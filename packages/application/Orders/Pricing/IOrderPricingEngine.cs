using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Orders.Pricing;

/// <summary>
/// Authoritative server-side pricing engine for validating and pricing customer/staff orders.
/// Strictly rejects client-supplied prices, names, and totals.
/// </summary>
public interface IOrderPricingEngine
{
    /// <summary>
    /// Performs atomic validation against active catalog, stock availability (86), branch settings,
    /// and dining session lifecycle, producing authoritative minor-unit financial snapshots.
    /// </summary>
    Task<PricedOrderResult> PriceAndValidateOrderAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        IReadOnlyList<ClientOrderItemDraft> items,
        string? clientPricingPolicyFingerprint = null,
        long? clientExpectedTotalMinorUnits = null,
        CancellationToken cancellationToken = default);
}
