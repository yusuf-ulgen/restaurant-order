using System.Collections.Frozen;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Machine-readable permission capability strings matching docs/ROLES-AND-PERMISSIONS.md.
/// Serves as the single source of truth for authorization capability identifiers in application code.
/// </summary>
public static class Permissions
{
    // Platform Management
    public const string PlatformTenantsManage = "platform.tenants.manage";
    public const string PlatformFeesManage = "platform.fees.manage";
    public const string PlatformAuditView = "platform.audit.view";

    // Brand & Branch Config
    public const string TenantBrandsManage = "tenant.brands.manage";
    public const string TenantBranchesManage = "tenant.branches.manage";
    public const string TenantBrandingView = "tenant.branding.view";
    public const string TenantBrandingManage = "tenant.branding.manage";
    public const string BranchPrintersManage = "branch.printers.manage";
    public const string BranchTablesManage = "branch.tables.manage";

    // Menu & Catalog
    public const string MenuCatalogManage = "menu.catalog.manage";
    public const string MenuPricingManage = "menu.pricing.manage";
    public const string MenuInventoryQuick86 = "menu.inventory.quick86";
    public const string MenuCatalogView = "menu.catalog.view";

    // Floor & Table Operations
    public const string FloorSessionsManage = "floor.sessions.manage";
    public const string FloorTablesMove = "floor.tables.move";
    public const string FloorStatusView = "floor.status.view";

    // Order & Ticket Lifecycle
    public const string OrdersQrCreate = "orders.qr.create";
    public const string OrdersStaffCreate = "orders.staff.create";
    public const string OrdersItemsCancelPrePrep = "orders.items.cancel_pre_prep";
    public const string OrdersItemsVoidInPrep = "orders.items.void_in_prep";

    // KDS Preparation
    public const string KdsKitchenView = "kds.kitchen.view";
    public const string KdsBarView = "kds.bar.view";
    public const string KdsTicketUpdate = "kds.ticket.update";
    public const string KdsTicketRecall = "kds.ticket.recall";

    // Billing & Payments
    public const string BillingBillRequest = "billing.bill.request";
    public const string BillingPaymentCash = "billing.payment.cash";
    public const string BillingPaymentPosCard = "billing.payment.pos_card";
    public const string BillingBillSplit = "billing.bill.split";
    public const string BillingDiscountApply = "billing.discount.apply";
    public const string BillingRefundAuthorize = "billing.refund.authorize";

    // Staff & Analytics
    public const string BranchStaffManage = "branch.staff.manage";
    public const string ReportsBranchRevenue = "reports.branch.revenue";
    public const string ReportsTenantMultiBranch = "reports.tenant.multi_branch";

    /// <summary>
    /// Frozen set of all 31 canonical permission capability strings.
    /// </summary>
    public static readonly FrozenSet<string> All = new[]
    {
        PlatformTenantsManage,
        PlatformFeesManage,
        PlatformAuditView,
        TenantBrandsManage,
        TenantBranchesManage,
        TenantBrandingView,
        TenantBrandingManage,
        BranchPrintersManage,
        BranchTablesManage,
        MenuCatalogManage,
        MenuPricingManage,
        MenuInventoryQuick86,
        MenuCatalogView,
        FloorSessionsManage,
        FloorTablesMove,
        FloorStatusView,
        OrdersQrCreate,
        OrdersStaffCreate,
        OrdersItemsCancelPrePrep,
        OrdersItemsVoidInPrep,
        KdsKitchenView,
        KdsBarView,
        KdsTicketUpdate,
        KdsTicketRecall,
        BillingBillRequest,
        BillingPaymentCash,
        BillingPaymentPosCard,
        BillingBillSplit,
        BillingDiscountApply,
        BillingRefundAuthorize,
        BranchStaffManage,
        ReportsBranchRevenue,
        ReportsTenantMultiBranch
    }.ToFrozenSet(StringComparer.Ordinal);
}
