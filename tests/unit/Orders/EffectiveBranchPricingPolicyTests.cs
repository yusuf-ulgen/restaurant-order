using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.Orders.Pricing;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Catalog;
using RestaurantOrder.Infrastructure.Orders;
using RestaurantOrder.Infrastructure.Orders.Pricing;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class EffectiveBranchPricingPolicyTests
{
    private static AuthenticatedPrincipal CreateCustomerActor(Guid tenantId, Guid branchId, Guid sessionId)
    {
        return AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: sessionId,
            tenantId: new TenantId(tenantId),
            branchId: new BranchId(branchId));
    }

    [Fact]
    public void PolicyParity_WhenBranchSettingsIsNull_UsesBasisPointsDefaultTaxAndDefaults()
    {
        var tenantId = TenantId.New();
        var brandId = BrandId.New();
        var branchId = BranchId.New();
        var brand = Brand.Create(tenantId, "Test Brand", "test-brand", id: brandId);
        var branch = Branch.Create(tenantId, brand, "Test Branch", "test-branch", "Europe/Istanbul", "EUR", id: branchId);

        var policy = EffectiveBranchPricingPolicy.Create(branch, null);

        Assert.Equal("EUR", policy.Currency);
        Assert.True(policy.PricesIncludeTax);
        Assert.Equal(BasisPointsRate.DefaultTaxRateBps, policy.DefaultTaxRateBps);
        Assert.Equal(1000, policy.DefaultTaxRateBps);
        Assert.False(policy.IsServiceChargeEnabled);
        Assert.Equal(0, policy.ServiceChargeRateBps);
        Assert.True(policy.IsOrderTakingEnabled);
        Assert.False(string.IsNullOrWhiteSpace(policy.PricingPolicyFingerprint));
        Assert.Equal(64, policy.PricingPolicyFingerprint.Length);
    }

    [Fact]
    public async Task PolicyParity_RuntimeMenuAndPricingEngine_NullSettings_MatchExactly()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        // Remove branch settings to test null settings fallback parity
        var existingSettings = await db.BranchSettings.FirstAsync(s => s.BranchId == branchId);
        db.BranchSettings.Remove(existingSettings);

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(10000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var catalogService = new CatalogService(db, NullLogger<CatalogService>.Instance);
        var actor = CreateCustomerActor(tenantId.Value, branchId.Value, sessionId.Value);
        var runtimeMenu = await catalogService.GetRuntimeMenuAsync(tenantId, branchId, actor, CancellationToken.None);

        var engine = new OrderPricingEngine(db);
        var drafts = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, new List<ClientSelectedModifierDraft>(), 10000)
        };
        var priced = await engine.PriceAndValidateOrderAsync(tenantId, branchId, sessionId, drafts, runtimeMenu.PricingPolicyFingerprint, 10000);

        Assert.NotNull(runtimeMenu);
        Assert.Equal(runtimeMenu.PricingPolicyFingerprint, priced.PricingPolicyFingerprint);
        Assert.True(runtimeMenu.PricesIncludeTax);
        Assert.Equal(BasisPointsRate.DefaultTaxRateBps, runtimeMenu.DefaultTaxRateBps);
        Assert.Equal(1000, runtimeMenu.DefaultTaxRateBps);
        Assert.False(runtimeMenu.IsServiceChargeEnabled);
        Assert.Equal(0, runtimeMenu.ServiceChargeRateBps);
        Assert.Equal("TRY", runtimeMenu.Currency);

        // Tax-inclusive 10% on 10000: tax = 10000 * 1000 / 11000 = 909 minor units
        Assert.Equal(909, priced.TaxMinorUnits);
        Assert.Equal(10000, priced.TotalMinorUnits);
    }

    [Fact]
    public async Task PolicyParity_RuntimeMenuAndPricingEngine_WithCustomSettings_MatchExactly()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment(
            isOrderTakingEnabled: true,
            pricesIncludeTax: false,
            defaultTaxRateBps: 2000,
            serviceChargeRateBps: 1500,
            currency: "USD");

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(10000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var catalogService = new CatalogService(db, NullLogger<CatalogService>.Instance);
        var actor = CreateCustomerActor(tenantId.Value, branchId.Value, sessionId.Value);
        var runtimeMenu = await catalogService.GetRuntimeMenuAsync(tenantId, branchId, actor, CancellationToken.None);

        var engine = new OrderPricingEngine(db);
        var drafts = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, new List<ClientSelectedModifierDraft>(), 10000)
        };
        var priced = await engine.PriceAndValidateOrderAsync(tenantId, branchId, sessionId, drafts, runtimeMenu.PricingPolicyFingerprint, 13800);

        Assert.NotNull(runtimeMenu);
        Assert.Equal(runtimeMenu.PricingPolicyFingerprint, priced.PricingPolicyFingerprint);
        Assert.False(runtimeMenu.PricesIncludeTax);
        Assert.Equal(2000, runtimeMenu.DefaultTaxRateBps);
        Assert.True(runtimeMenu.IsServiceChargeEnabled);
        Assert.Equal(1500, runtimeMenu.ServiceChargeRateBps);
        Assert.Equal("USD", runtimeMenu.Currency);

        // Subtotal: 10000
        // Tax-exclusive 20%: 2000 -> Gross: 12000
        // Service charge 15% on post-tax gross (12000 * 1500 / 10000): 1800
        // Total: 13800
        Assert.Equal(10000, priced.SubtotalMinorUnits);
        Assert.Equal(2000, priced.TaxMinorUnits);
        Assert.Equal(1800, priced.ServiceChargeMinorUnits);
        Assert.Equal(13800, priced.TotalMinorUnits);
    }

    [Fact]
    public async Task PricingEngine_WhenPolicyFingerprintMismatches_Throws409PricingPolicyChanged()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(5000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);
        var drafts = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, new List<ClientSelectedModifierDraft>(), 5000)
        };

        var stalePolicyHash = "0000000000000000000000000000000000000000000000000000000000000000";
        var ex = await Assert.ThrowsAsync<OrderPricingException>(() =>
            engine.PriceAndValidateOrderAsync(
                tenantId,
                branchId,
                sessionId,
                drafts,
                clientPricingPolicyFingerprint: stalePolicyHash,
                clientExpectedTotalMinorUnits: 5000));

        Assert.Equal(OrderPricingException.CodePricingPolicyChanged, ex.ErrorCode);
    }

    [Fact]
    public async Task PricingEngine_WhenExpectedTotalMismatches_Throws409OrderTotalChanged()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(5000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var catalogService = new CatalogService(db, NullLogger<CatalogService>.Instance);
        var actor = CreateCustomerActor(tenantId.Value, branchId.Value, sessionId.Value);
        var runtimeMenu = await catalogService.GetRuntimeMenuAsync(tenantId, branchId, actor, CancellationToken.None);
        Assert.NotNull(runtimeMenu);

        var engine = new OrderPricingEngine(db);
        var drafts = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, new List<ClientSelectedModifierDraft>(), 5000)
        };

        // Client expects 4000 instead of authoritative 5000
        var ex = await Assert.ThrowsAsync<OrderPricingException>(() =>
            engine.PriceAndValidateOrderAsync(
                tenantId,
                branchId,
                sessionId,
                drafts,
                clientPricingPolicyFingerprint: runtimeMenu.PricingPolicyFingerprint,
                clientExpectedTotalMinorUnits: 4000));

        Assert.Equal(OrderPricingException.CodeOrderTotalChanged, ex.ErrorCode);
    }

    [Fact]
    public async Task CreateCustomerOrderAsync_PolicyOrTotalMismatch_GuaranteesZeroPersistence()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(5000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var catalogService = new CatalogService(db, NullLogger<CatalogService>.Instance);
        var actor = CreateCustomerActor(tenantId.Value, branchId.Value, sessionId.Value);
        var runtimeMenu = await catalogService.GetRuntimeMenuAsync(tenantId, branchId, actor, CancellationToken.None);
        Assert.NotNull(runtimeMenu);

        var engine = new OrderPricingEngine(db);
        var customerService = new CustomerOrderService(db, engine);

        var initialOrdersCount = await db.Orders.CountAsync();
        var initialOrderItemsCount = await db.OrderItems.CountAsync();

        // 1. Policy mismatch
        var badPolicyReq = new CreateCustomerOrderRequest(
            new[] { new CustomerOrderItemRequest(item.Id.Value, null, 1, null, null, 5000) },
            ClientPricingPolicyFingerprint: "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef",
            ClientExpectedTotalMinorUnits: 5000);

        await Assert.ThrowsAsync<OrderPricingException>(() =>
            customerService.CreateOrderAsync(tenantId, branchId, sessionId, badPolicyReq, "key-bad-pol", actor));

        Assert.Equal(initialOrdersCount, await db.Orders.CountAsync());
        Assert.Equal(initialOrderItemsCount, await db.OrderItems.CountAsync());

        // 2. Total mismatch
        var badTotalReq = new CreateCustomerOrderRequest(
            new[] { new CustomerOrderItemRequest(item.Id.Value, null, 1, null, null, 5000) },
            ClientPricingPolicyFingerprint: runtimeMenu.PricingPolicyFingerprint,
            ClientExpectedTotalMinorUnits: 9999);

        await Assert.ThrowsAsync<OrderPricingException>(() =>
            customerService.CreateOrderAsync(tenantId, branchId, sessionId, badTotalReq, "key-bad-tot", actor));

        Assert.Equal(initialOrdersCount, await db.Orders.CountAsync());
        Assert.Equal(initialOrderItemsCount, await db.OrderItems.CountAsync());
    }
}
