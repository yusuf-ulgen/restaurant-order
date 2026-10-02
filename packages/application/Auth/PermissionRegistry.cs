using System.Collections.Frozen;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Canonical implementation of the centralized RBAC capability registry.
/// Encodes the complete 8-role x 31-capability matrix defined in docs/ROLES-AND-PERMISSIONS.md.
/// Strictly enforces deny-by-default for unmapped or unknown roles/permissions.
/// </summary>
public sealed class PermissionRegistry : IPermissionRegistry
{
    private readonly IResourceOwnershipRequirement _ownershipRequirement;

    private static readonly FrozenDictionary<(AuthRole Role, string Permission), PermissionGrantType> Matrix =
        BuildMatrix();

    private static readonly FrozenDictionary<AuthRole, FrozenSet<string>> RolePermissions =
        BuildRolePermissions();

    public PermissionRegistry(IResourceOwnershipRequirement? ownershipRequirement = null)
    {
        _ownershipRequirement = ownershipRequirement ?? new DefaultResourceOwnershipRequirement();
    }

    public PermissionGrantType GetGrantType(AuthRole role, string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return PermissionGrantType.Denied;
        }

        return Matrix.TryGetValue((role, permission), out var grant)
            ? grant
            : PermissionGrantType.Denied;
    }

    public bool HasFullGrant(AuthRole role, string permission) =>
        GetGrantType(role, permission) == PermissionGrantType.Full;

    public bool IsPermitted(
        AuthenticatedPrincipal principal,
        string permission,
        ResourceOwnershipContext? resourceContext = null)
    {
        if (principal == null || string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        var grantType = GetGrantType(principal.Role, permission);
        return grantType switch
        {
            PermissionGrantType.Full => true,
            PermissionGrantType.OwnOrAssigned => resourceContext != null &&
                _ownershipRequirement.Satisfies(principal, permission, resourceContext),
            _ => false
        };
    }

    public IReadOnlyCollection<string> GetPermissionsForRole(AuthRole role)
    {
        return RolePermissions.TryGetValue(role, out var permissions)
            ? permissions
            : Array.Empty<string>();
    }

    private static FrozenDictionary<(AuthRole, string), PermissionGrantType> BuildMatrix()
    {
        var map = new Dictionary<(AuthRole, string), PermissionGrantType>();

        void Grant(AuthRole role, string permission, PermissionGrantType grant) =>
            map[(role, permission)] = grant;

        void Full(AuthRole role, string permission) =>
            Grant(role, permission, PermissionGrantType.Full);

        void Own(AuthRole role, string permission) =>
            Grant(role, permission, PermissionGrantType.OwnOrAssigned);

        // --- SuperAdmin ---
        Full(AuthRole.SuperAdmin, Permissions.PlatformTenantsManage);
        Full(AuthRole.SuperAdmin, Permissions.PlatformFeesManage);
        Full(AuthRole.SuperAdmin, Permissions.PlatformAuditView);
        Full(AuthRole.SuperAdmin, Permissions.MenuCatalogView);

        // --- RestaurantAdmin ---
        Full(AuthRole.RestaurantAdmin, Permissions.TenantBrandsManage);
        Full(AuthRole.RestaurantAdmin, Permissions.TenantBranchesManage);
        Full(AuthRole.RestaurantAdmin, Permissions.BranchPrintersManage);
        Full(AuthRole.RestaurantAdmin, Permissions.BranchTablesManage);
        Full(AuthRole.RestaurantAdmin, Permissions.MenuCatalogManage);
        Full(AuthRole.RestaurantAdmin, Permissions.MenuPricingManage);
        Full(AuthRole.RestaurantAdmin, Permissions.MenuInventoryQuick86);
        Full(AuthRole.RestaurantAdmin, Permissions.MenuCatalogView);
        Full(AuthRole.RestaurantAdmin, Permissions.FloorSessionsManage);
        Full(AuthRole.RestaurantAdmin, Permissions.FloorTablesMove);
        Full(AuthRole.RestaurantAdmin, Permissions.FloorStatusView);
        Full(AuthRole.RestaurantAdmin, Permissions.OrdersStaffCreate);
        Full(AuthRole.RestaurantAdmin, Permissions.OrdersItemsCancelPrePrep);
        Full(AuthRole.RestaurantAdmin, Permissions.OrdersItemsVoidInPrep);
        Full(AuthRole.RestaurantAdmin, Permissions.KdsKitchenView);
        Full(AuthRole.RestaurantAdmin, Permissions.KdsBarView);
        Full(AuthRole.RestaurantAdmin, Permissions.BillingPaymentCash);
        Full(AuthRole.RestaurantAdmin, Permissions.BillingPaymentPosCard);
        Full(AuthRole.RestaurantAdmin, Permissions.BillingBillSplit);
        Full(AuthRole.RestaurantAdmin, Permissions.BillingDiscountApply);
        Full(AuthRole.RestaurantAdmin, Permissions.BillingRefundAuthorize);
        Full(AuthRole.RestaurantAdmin, Permissions.BranchStaffManage);
        Full(AuthRole.RestaurantAdmin, Permissions.ReportsBranchRevenue);
        Full(AuthRole.RestaurantAdmin, Permissions.ReportsTenantMultiBranch);

        // --- BranchManager ---
        Full(AuthRole.BranchManager, Permissions.BranchPrintersManage);
        Full(AuthRole.BranchManager, Permissions.BranchTablesManage);
        Full(AuthRole.BranchManager, Permissions.MenuCatalogManage);
        Full(AuthRole.BranchManager, Permissions.MenuPricingManage);
        Full(AuthRole.BranchManager, Permissions.MenuInventoryQuick86);
        Full(AuthRole.BranchManager, Permissions.MenuCatalogView);
        Full(AuthRole.BranchManager, Permissions.FloorSessionsManage);
        Full(AuthRole.BranchManager, Permissions.FloorTablesMove);
        Full(AuthRole.BranchManager, Permissions.FloorStatusView);
        Full(AuthRole.BranchManager, Permissions.OrdersStaffCreate);
        Full(AuthRole.BranchManager, Permissions.OrdersItemsCancelPrePrep);
        Full(AuthRole.BranchManager, Permissions.OrdersItemsVoidInPrep);
        Full(AuthRole.BranchManager, Permissions.KdsKitchenView);
        Full(AuthRole.BranchManager, Permissions.KdsBarView);
        Full(AuthRole.BranchManager, Permissions.KdsTicketUpdate);
        Full(AuthRole.BranchManager, Permissions.KdsTicketRecall);
        Full(AuthRole.BranchManager, Permissions.BillingPaymentCash);
        Full(AuthRole.BranchManager, Permissions.BillingPaymentPosCard);
        Full(AuthRole.BranchManager, Permissions.BillingBillSplit);
        Full(AuthRole.BranchManager, Permissions.BillingDiscountApply);
        Full(AuthRole.BranchManager, Permissions.BillingRefundAuthorize);
        Full(AuthRole.BranchManager, Permissions.BranchStaffManage);
        Full(AuthRole.BranchManager, Permissions.ReportsBranchRevenue);

        // --- Cashier ---
        Full(AuthRole.Cashier, Permissions.MenuInventoryQuick86);
        Full(AuthRole.Cashier, Permissions.MenuCatalogView);
        Full(AuthRole.Cashier, Permissions.FloorSessionsManage);
        Full(AuthRole.Cashier, Permissions.FloorTablesMove);
        Full(AuthRole.Cashier, Permissions.FloorStatusView);
        Full(AuthRole.Cashier, Permissions.OrdersStaffCreate);
        Full(AuthRole.Cashier, Permissions.OrdersItemsCancelPrePrep);
        Full(AuthRole.Cashier, Permissions.BillingPaymentCash);
        Full(AuthRole.Cashier, Permissions.BillingPaymentPosCard);
        Full(AuthRole.Cashier, Permissions.BillingBillSplit);
        Own(AuthRole.Cashier, Permissions.ReportsBranchRevenue);

        // --- Kitchen ---
        Full(AuthRole.Kitchen, Permissions.MenuInventoryQuick86);
        Full(AuthRole.Kitchen, Permissions.MenuCatalogView);
        Full(AuthRole.Kitchen, Permissions.KdsKitchenView);
        Own(AuthRole.Kitchen, Permissions.KdsTicketUpdate);
        Own(AuthRole.Kitchen, Permissions.KdsTicketRecall);

        // --- Bar ---
        Full(AuthRole.Bar, Permissions.MenuInventoryQuick86);
        Full(AuthRole.Bar, Permissions.MenuCatalogView);
        Full(AuthRole.Bar, Permissions.KdsBarView);
        Own(AuthRole.Bar, Permissions.KdsTicketUpdate);
        Own(AuthRole.Bar, Permissions.KdsTicketRecall);

        // --- Waiter ---
        Full(AuthRole.Waiter, Permissions.MenuCatalogView);
        Full(AuthRole.Waiter, Permissions.FloorSessionsManage);
        Full(AuthRole.Waiter, Permissions.FloorTablesMove);
        Full(AuthRole.Waiter, Permissions.FloorStatusView);
        Full(AuthRole.Waiter, Permissions.OrdersStaffCreate);
        Full(AuthRole.Waiter, Permissions.OrdersItemsCancelPrePrep);
        Full(AuthRole.Waiter, Permissions.BillingBillRequest);
        Own(AuthRole.Waiter, Permissions.BillingPaymentPosCard);
        Full(AuthRole.Waiter, Permissions.BillingBillSplit);

        // --- Customer ---
        Full(AuthRole.Customer, Permissions.MenuCatalogView);
        Own(AuthRole.Customer, Permissions.FloorSessionsManage);
        Full(AuthRole.Customer, Permissions.OrdersQrCreate);
        Full(AuthRole.Customer, Permissions.BillingBillRequest);

        return map.ToFrozenDictionary();
    }

    private static FrozenDictionary<AuthRole, FrozenSet<string>> BuildRolePermissions()
    {
        var result = new Dictionary<AuthRole, HashSet<string>>();
        foreach (var ((role, perm), grant) in Matrix)
        {
            if (grant != PermissionGrantType.Denied)
            {
                if (!result.TryGetValue(role, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    result[role] = set;
                }
                set.Add(perm);
            }
        }

        return result.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToFrozenSet(StringComparer.Ordinal)
        ).ToFrozenDictionary();
    }
}
