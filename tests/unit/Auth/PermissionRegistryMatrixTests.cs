using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class PermissionRegistryMatrixTests
{
    private readonly IPermissionRegistry _registry = new PermissionRegistry();

    [Fact]
    public void Permissions_All_ContainsExactlyThirtyThreePermissions()
    {
        Assert.Equal(33, Permissions.All.Count);
    }

    [Theory]
    [MemberData(nameof(GetMatrixTestData))]
    public void PermissionRegistry_EvaluatesEveryCellInMatrix_Correctly(
        AuthRole role,
        string permission,
        PermissionGrantType expectedGrant)
    {
        var actualGrant = _registry.GetGrantType(role, permission);
        Assert.Equal(expectedGrant, actualGrant);

        var hasFull = _registry.HasFullGrant(role, permission);
        Assert.Equal(expectedGrant == PermissionGrantType.Full, hasFull);
    }

    [Theory]
    [InlineData(AuthRole.SuperAdmin, 4)]
    [InlineData(AuthRole.RestaurantAdmin, 26)]
    [InlineData(AuthRole.BranchManager, 25)]
    [InlineData(AuthRole.Cashier, 12)]
    [InlineData(AuthRole.Kitchen, 6)]
    [InlineData(AuthRole.Bar, 6)]
    [InlineData(AuthRole.Waiter, 10)]
    [InlineData(AuthRole.Customer, 5)]
    public void PermissionRegistry_GetPermissionsForRole_ReturnsExactCount(AuthRole role, int expectedCount)
    {
        var permissions = _registry.GetPermissionsForRole(role);
        Assert.Equal(expectedCount, permissions.Count);
    }

    [Fact]
    public void PermissionRegistry_DenyByDefault_ForUnknownOrEmptyPermissions()
    {
        foreach (var role in Enum.GetValues<AuthRole>())
        {
            Assert.Equal(PermissionGrantType.Denied, _registry.GetGrantType(role, ""));
            Assert.Equal(PermissionGrantType.Denied, _registry.GetGrantType(role, "   "));
            Assert.Equal(PermissionGrantType.Denied, _registry.GetGrantType(role, null!));
            Assert.Equal(PermissionGrantType.Denied, _registry.GetGrantType(role, "unmapped.custom.action"));
            Assert.False(_registry.HasFullGrant(role, "unmapped.custom.action"));
        }

        // Unknown role enum value
        Assert.Equal(
            PermissionGrantType.Denied,
            _registry.GetGrantType((AuthRole)999, Permissions.PlatformTenantsManage));
        Assert.Empty(_registry.GetPermissionsForRole((AuthRole)999));
    }

    public static TheoryData<AuthRole, string, PermissionGrantType> GetMatrixTestData()
    {
        var data = new TheoryData<AuthRole, string, PermissionGrantType>();

        foreach (var role in Enum.GetValues<AuthRole>())
        {
            foreach (var perm in Permissions.All)
            {
                var expected = GetExpectedGrant(role, perm);
                data.Add(role, perm, expected);
            }
        }

        return data;
    }

    private static PermissionGrantType GetExpectedGrant(AuthRole role, string perm)
    {
        return role switch
        {
            AuthRole.SuperAdmin => perm switch
            {
                Permissions.PlatformTenantsManage or
                Permissions.PlatformFeesManage or
                Permissions.PlatformAuditView or
                Permissions.MenuCatalogView => PermissionGrantType.Full,
                _ => PermissionGrantType.Denied
            },

            AuthRole.RestaurantAdmin => perm switch
            {
                Permissions.PlatformTenantsManage or
                Permissions.PlatformFeesManage or
                Permissions.PlatformAuditView or
                Permissions.OrdersQrCreate or
                Permissions.KdsTicketUpdate or
                Permissions.KdsTicketRecall or
                Permissions.BillingBillRequest => PermissionGrantType.Denied,
                _ => PermissionGrantType.Full
            },

            AuthRole.BranchManager => perm switch
            {
                Permissions.PlatformTenantsManage or
                Permissions.PlatformFeesManage or
                Permissions.PlatformAuditView or
                Permissions.TenantBrandsManage or
                Permissions.TenantBranchesManage or
                Permissions.OrdersQrCreate or
                Permissions.BillingBillRequest or
                Permissions.ReportsTenantMultiBranch => PermissionGrantType.Denied,
                Permissions.TenantBrandingManage => PermissionGrantType.OwnOrAssigned,
                _ => PermissionGrantType.Full
            },

            AuthRole.Cashier => perm switch
            {
                Permissions.MenuInventoryQuick86 or
                Permissions.MenuCatalogView or
                Permissions.FloorSessionsManage or
                Permissions.FloorTablesMove or
                Permissions.FloorStatusView or
                Permissions.OrdersStaffCreate or
                Permissions.OrdersItemsCancelPrePrep or
                Permissions.BillingPaymentCash or
                Permissions.BillingPaymentPosCard or
                Permissions.BillingBillSplit or
                Permissions.TenantBrandingView => PermissionGrantType.Full,
                Permissions.ReportsBranchRevenue => PermissionGrantType.OwnOrAssigned,
                _ => PermissionGrantType.Denied
            },

            AuthRole.Kitchen => perm switch
            {
                Permissions.MenuInventoryQuick86 or
                Permissions.MenuCatalogView or
                Permissions.KdsKitchenView or
                Permissions.TenantBrandingView => PermissionGrantType.Full,
                Permissions.KdsTicketUpdate or
                Permissions.KdsTicketRecall => PermissionGrantType.OwnOrAssigned,
                _ => PermissionGrantType.Denied
            },

            AuthRole.Bar => perm switch
            {
                Permissions.MenuInventoryQuick86 or
                Permissions.MenuCatalogView or
                Permissions.KdsBarView or
                Permissions.TenantBrandingView => PermissionGrantType.Full,
                Permissions.KdsTicketUpdate or
                Permissions.KdsTicketRecall => PermissionGrantType.OwnOrAssigned,
                _ => PermissionGrantType.Denied
            },

            AuthRole.Waiter => perm switch
            {
                Permissions.MenuCatalogView or
                Permissions.FloorSessionsManage or
                Permissions.FloorTablesMove or
                Permissions.FloorStatusView or
                Permissions.OrdersStaffCreate or
                Permissions.OrdersItemsCancelPrePrep or
                Permissions.BillingBillRequest or
                Permissions.BillingBillSplit or
                Permissions.TenantBrandingView => PermissionGrantType.Full,
                Permissions.BillingPaymentPosCard => PermissionGrantType.OwnOrAssigned,
                _ => PermissionGrantType.Denied
            },

            AuthRole.Customer => perm switch
            {
                Permissions.MenuCatalogView or
                Permissions.OrdersQrCreate or
                Permissions.BillingBillRequest or
                Permissions.TenantBrandingView => PermissionGrantType.Full,
                Permissions.FloorSessionsManage => PermissionGrantType.OwnOrAssigned,
                _ => PermissionGrantType.Denied
            },

            _ => PermissionGrantType.Denied
        };
    }
}
