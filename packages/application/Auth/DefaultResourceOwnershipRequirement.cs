using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Canonical implementation of resource ownership and station assignment requirements.
/// Implements deny-by-default logic for all 'OwnOrAssigned' capability checks.
/// </summary>
public sealed class DefaultResourceOwnershipRequirement : IResourceOwnershipRequirement
{
    public bool Satisfies(
        AuthenticatedPrincipal principal,
        string permission,
        ResourceOwnershipContext context)
    {
        if (principal == null || context == null)
        {
            return false;
        }

        return principal.Role switch
        {
            AuthRole.Customer => EvaluateCustomerOwnership(principal, permission, context),
            AuthRole.Waiter => EvaluateWaiterOwnership(principal, permission, context),
            AuthRole.Cashier => EvaluateCashierOwnership(principal, permission, context),
            AuthRole.Kitchen => EvaluateKitchenOwnership(principal, permission, context),
            AuthRole.Bar => EvaluateBarOwnership(principal, permission, context),
            AuthRole.BranchManager => EvaluateBranchManagerOwnership(principal, permission, context),
            _ => false
        };
    }

    private static bool EvaluateBranchManagerOwnership(
        AuthenticatedPrincipal principal,
        string permission,
        ResourceOwnershipContext context)
    {
        if (permission is Permissions.BranchConfigurationManage or Permissions.TenantBrandingManage)
        {
            return context.BranchId.HasValue
                && principal.Scope.BranchId.HasValue
                && context.BranchId.Value == principal.Scope.BranchId.Value;
        }

        return false;
    }

    private static bool EvaluateCustomerOwnership(
        AuthenticatedPrincipal principal,
        string permission,
        ResourceOwnershipContext context)
    {
        if (permission == Permissions.FloorSessionsManage)
        {
            return context.TableSessionId.HasValue
                && principal.TableSessionId.HasValue
                && context.TableSessionId.Value == principal.TableSessionId.Value;
        }

        return false;
    }

    private static bool EvaluateWaiterOwnership(
        AuthenticatedPrincipal principal,
        string permission,
        ResourceOwnershipContext context)
    {
        if (permission == Permissions.BillingPaymentPosCard)
        {
            return context.AssignedStaffId == principal.SubjectId
                || context.OwnerStaffId == principal.SubjectId;
        }

        return false;
    }

    private static bool EvaluateCashierOwnership(
        AuthenticatedPrincipal principal,
        string permission,
        ResourceOwnershipContext context)
    {
        if (permission == Permissions.ReportsBranchRevenue)
        {
            return context.OwnerStaffId == principal.SubjectId;
        }

        return false;
    }

    private static bool EvaluateKitchenOwnership(
        AuthenticatedPrincipal principal,
        string permission,
        ResourceOwnershipContext context)
    {
        if (permission is Permissions.KdsTicketUpdate or Permissions.KdsTicketRecall)
        {
            return string.Equals(context.Station, "kitchen", StringComparison.OrdinalIgnoreCase)
                || context.AssignedStaffId == principal.SubjectId;
        }

        return false;
    }

    private static bool EvaluateBarOwnership(
        AuthenticatedPrincipal principal,
        string permission,
        ResourceOwnershipContext context)
    {
        if (permission is Permissions.KdsTicketUpdate or Permissions.KdsTicketRecall)
        {
            return string.Equals(context.Station, "bar", StringComparison.OrdinalIgnoreCase)
                || context.AssignedStaffId == principal.SubjectId;
        }

        return false;
    }
}
