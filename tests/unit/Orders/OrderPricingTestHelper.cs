using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.UnitTests.Orders;

public static class OrderPricingTestHelper
{
    public const string DefaultTestPolicyFingerprint = "0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9";

    public static (RestaurantOrderDbContext DbContext, TenantId TenantId, BranchId BranchId, DiningSessionId SessionId) CreateTestEnvironment(
        bool isOrderTakingEnabled = true,
        bool pricesIncludeTax = true,
        int defaultTaxRateBps = 1000,
        int serviceChargeRateBps = 0,
        string currency = "TRY",
        bool sessionActive = true)
    {
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var brandId = BrandId.New();
        var diningAreaId = DiningAreaId.New();

        var dbName = $"OrderPricingDb_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var tenantContext = new TenantContext(tenantId.Value, isAuthenticated: true);
        var db = new RestaurantOrderDbContext(options, tenantContext);

        var tenant = Tenant.Create("Test Tenant", $"t-{Guid.NewGuid():N}", id: tenantId);
        db.Tenants.Add(tenant);

        var brand = Brand.Create(tenantId, "Test Brand", $"br-{Guid.NewGuid():N}", id: brandId);
        db.Brands.Add(brand);

        var branch = Branch.Create(tenantId, brand, "Test Branch", $"b-{Guid.NewGuid():N}", "Europe/Istanbul", currency, id: branchId);
        db.Branches.Add(branch);

        var branchSettings = BranchSettings.Create(
            tenantId,
            branchId,
            currency: currency,
            pricesIncludeTax: pricesIncludeTax,
            defaultTaxRateBps: defaultTaxRateBps,
            isServiceChargeEnabled: serviceChargeRateBps > 0,
            serviceChargeRateBps: serviceChargeRateBps,
            isOrderTakingEnabled: isOrderTakingEnabled);
        db.BranchSettings.Add(branchSettings);

        var area = DiningArea.Create(tenantId, branchId, "Main Hall", $"area-{Guid.NewGuid():N}", DiningAreaType.Indoor, 1, diningAreaId);
        db.DiningAreas.Add(area);

        var table = RestaurantTable.Create(tenantId, branchId, diningAreaId, "T-10", "Table 10", 4);
        db.RestaurantTables.Add(table);

        var session = DiningSession.Open(tenantId, branchId, table, 2);
        if (sessionActive)
        {
            session.Activate();
        }
        else
        {
            session.Activate();
            session.RequestBill();
            session.Close("Test closed");
        }
        db.DiningSessions.Add(session);

        db.SaveChanges();

        return (db, tenantId, branchId, session.Id);
    }
}
