using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;

namespace RestaurantOrder.Infrastructure.Orders;

public sealed partial class StaffOrderService
{
    private static void ValidateStaffActor(AuthenticatedPrincipal actor, BranchId branchId)
    {
        if (actor == null)
        {
            throw new InvalidAuthorizationScopeException("Authentication required.");
        }

        if (actor.Role == AuthRole.Customer)
        {
            throw new InvalidAuthorizationScopeException("Customer cannot access staff order endpoints.");
        }

        if (!actor.Scope.TenantId.HasValue || actor.Scope.TenantId.Value.Value == Guid.Empty)
        {
            throw new InvalidAuthorizationScopeException("Actor is missing required tenant scope.");
        }

        if (actor.Role is AuthRole.Waiter or AuthRole.Cashier or AuthRole.BranchManager)
        {
            if (!actor.Scope.BranchId.HasValue || actor.Scope.BranchId.Value != branchId)
            {
                throw new InvalidAuthorizationScopeException("Staff member is not assigned to the requested branch.");
            }
        }
    }

    private static string ComputeRequestFingerprint(DiningSessionId sessionId, CreateStaffOrderRequest request)
    {
        return OrderFingerprintGenerator.ComputeStaffOrderFingerprint(
            sessionId.Value,
            request.Items,
            request.ClientPricingPolicyFingerprint,
            request.ClientExpectedTotalMinorUnits);
    }

    private static StaffOrderResponseDto MapToDto(Order order)
    {
        var items = order.Items.Select(i => new StaffOrderItemDto(
            i.Id.Value,
            i.MenuItemId.Value,
            i.ItemVariantId?.Value,
            i.ItemName,
            i.VariantName,
            i.Quantity,
            i.CustomerNote,
            i.PreparationStationId,
            i.UnitPriceMinorUnits,
            i.ModifierTotalMinorUnits,
            i.LineTotalMinorUnits,
            i.TaxRateBasisPoints,
            i.Status.ToString(),
            i.CreatedAtUtc,
            i.UpdatedAtUtc,
            i.Modifiers.Select(m => new StaffOrderItemModifierDto(
                m.Id,
                m.ModifierGroupId,
                m.ModifierGroupName,
                m.ModifierOptionId,
                m.ModifierOptionName,
                m.PriceDeltaMinorUnits)).ToList())).ToList();

        return new StaffOrderResponseDto(
            order.Id.Value,
            order.TenantId.Value,
            order.BranchId.Value,
            order.DiningSessionId.Value,
            order.TableId.Value,
            order.RoundNumber,
            order.OrderSource.ToString(),
            order.Status.ToString(),
            order.Currency.Code,
            order.SubtotalMinorUnits,
            order.TaxMinorUnits,
            order.ServiceChargeMinorUnits,
            order.TotalMinorUnits,
            order.IdempotencyKey,
            order.ConcurrencyToken,
            order.CreatedAtUtc,
            order.SubmittedAtUtc,
            order.UpdatedAtUtc,
            order.CancellationReason,
            order.CancelledByActorId,
            items);
    }
}
