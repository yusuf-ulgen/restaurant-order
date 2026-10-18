using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Orders.Pricing;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Infrastructure.Orders.Pricing;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class OrderPricingEngineTests
{
    [Fact]
    public async Task PriceOrderAsync_BasicActiveItem_PricesSuccessfully()
    {
        // Arrange
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment(
            isOrderTakingEnabled: true,
            pricesIncludeTax: true,
            defaultTaxRateBps: 1000); // 10%

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);

        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);

        var item = MenuItem.Create(
            tenantId,
            branchId,
            menu.Id,
            cat.Id,
            "Cheeseburger",
            "cheeseburger",
            PriceAmount.FromMinorUnits(2000),
            sortOrder: 1,
            isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);

        var items = new List<ClientOrderItemDraft>
        {
            new(
                item.Id,
                null,
                2,
                "Extra napkin",
                new List<ClientSelectedModifierDraft>(),
                2000)
        };

        // Act
        var result = await engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 4000);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.LineItems);
        var line = result.LineItems[0];
        Assert.Equal(item.Id, line.MenuItemId);
        Assert.Equal("Cheeseburger", line.ItemName);
        Assert.Equal(2000, line.UnitPriceMinorUnits);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(4000, line.LineTotalMinorUnits);
        Assert.Equal(364, line.TaxMinorUnits);
        Assert.Equal(3636, line.NetSubtotalMinorUnits);
        Assert.Equal(4000, result.TotalMinorUnits);
        Assert.Equal("TRY", result.Currency.Code);
    }

    [Fact]
    public async Task PriceOrderAsync_WithVariant_UsesVariantPrice()
    {
        // Arrange
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);

        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);

        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);

        var variant = ItemVariant.Create(
            tenantId,
            branchId,
            menu.Id,
            item.Id,
            "Double Patty",
            "DBL",
            PriceAmount.FromMinorUnits(2800),
            sortOrder: 1,
            isActive: true);
        db.ItemVariants.Add(variant);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);

        var items = new List<ClientOrderItemDraft>
        {
            new(item.Id, variant.Id, 1, null, new List<ClientSelectedModifierDraft>(), 2800)
        };

        // Act
        var result = await engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 2800);

        // Assert
        Assert.Equal(2800, result.LineItems[0].UnitPriceMinorUnits);
        Assert.Equal("Double Patty", result.LineItems[0].VariantName);
        Assert.Equal(2800, result.TotalMinorUnits);
    }

    [Fact]
    public async Task PriceOrderAsync_WithModifiers_AddsModifierPriceDeltas()
    {
        // Arrange
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);

        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);

        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);

        var group = ModifierGroup.Create(tenantId, branchId, "Cheese", 0, 2, sortOrder: 1, isActive: true);
        db.ModifierGroups.Add(group);

        var assign = MenuItemModifierGroupAssignment.Create(tenantId, branchId, menu.Id, item.Id, group.Id, sortOrder: 1);
        db.MenuItemModifierGroupAssignments.Add(assign);

        var optCheddar = ModifierOption.Create(tenantId, branchId, group.Id, "Cheddar", PriceAmount.FromMinorUnits(300), sortOrder: 1, isActive: true);
        var optBacon = ModifierOption.Create(tenantId, branchId, group.Id, "Bacon", PriceAmount.FromMinorUnits(400), sortOrder: 2, isActive: true);
        db.ModifierOptions.AddRange(optCheddar, optBacon);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);

        var items = new List<ClientOrderItemDraft>
        {
            new(
                item.Id,
                null,
                1,
                null,
                new List<ClientSelectedModifierDraft>
                {
                    new(group.Id.Value, optCheddar.Id.Value),
                    new(group.Id.Value, optBacon.Id.Value)
                },
                2700) // 2000 + 300 + 400 = 2700
        };

        // Act
        var result = await engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 2700);

        // Assert
        Assert.Equal(2000, result.LineItems[0].UnitPriceMinorUnits);
        Assert.Equal(700, result.LineItems[0].ModifierTotalMinorUnits);
        Assert.Equal(2700, result.LineItems[0].LineTotalMinorUnits);
        Assert.Equal(2, result.LineItems[0].Modifiers.Count);
        Assert.Equal(2700, result.TotalMinorUnits);
    }

    [Fact]
    public async Task PriceOrderAsync_WhenPricesExcludeTax_AddsTaxOnTop()
    {
        // Arrange
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment(
            isOrderTakingEnabled: true,
            pricesIncludeTax: false,
            defaultTaxRateBps: 2000); // 20% tax

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);

        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Drinks", "drinks", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);

        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Cola", "cola", PriceAmount.FromMinorUnits(1000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);

        var items = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, new List<ClientSelectedModifierDraft>(), 1000)
        };

        // Act
        var branch = await db.Branches.FirstAsync(b => b.Id == branchId);
        var settings = await db.BranchSettings.FirstOrDefaultAsync(s => s.BranchId == branchId);
        var policy = EffectiveBranchPricingPolicy.Create(branch, settings);
        var result = await engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, policy.PricingPolicyFingerprint, 1200);

        // Assert: 1000 net + 200 tax = 1200 total
        Assert.Equal(1000, result.SubtotalMinorUnits);
        Assert.Equal(200, result.TaxMinorUnits);
        Assert.Equal(1200, result.TotalMinorUnits);
    }

    [Fact]
    public async Task PriceOrderAsync_WhenServiceChargeEnabled_CalculatesServiceChargeOnGross()
    {
        // Arrange: 10% tax included, 10% service charge (1000 bps)
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment(
            isOrderTakingEnabled: true,
            pricesIncludeTax: true,
            defaultTaxRateBps: 1000,
            serviceChargeRateBps: 1000);

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);

        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);

        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);

        var items = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, new List<ClientSelectedModifierDraft>(), 2000)
        };

        // Act
        var branch = await db.Branches.FirstAsync(b => b.Id == branchId);
        var settings = await db.BranchSettings.FirstOrDefaultAsync(s => s.BranchId == branchId);
        var policy = EffectiveBranchPricingPolicy.Create(branch, settings);
        var result = await engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, policy.PricingPolicyFingerprint, 2200);

        // Assert: 2000 gross + 200 service charge = 2200 total
        Assert.Equal(200, result.ServiceChargeMinorUnits);
        Assert.Equal(2200, result.TotalMinorUnits);
    }

    [Fact]
    public async Task PriceOrderAsync_WhenClientExpectedPriceDiffers_ThrowsOrderPriceChangedException()
    {
        // Arrange
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);

        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);

        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2500), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);

        var items = new List<ClientOrderItemDraft>
        {
            new(
                item.Id,
                null,
                1,
                null,
                new List<ClientSelectedModifierDraft>(),
                2000) // Client expects 2000, authoritative price is 2500
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<OrderPricingException>(() => engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 2000));
        Assert.Equal(OrderPricingException.CodePriceChanged, ex.ErrorCode);
        Assert.Equal(2000, ex.ExpectedUnitPriceMinorUnits);
        Assert.Equal(2500, ex.ActualUnitPriceMinorUnits);
    }

    [Fact]
    public async Task PriceOrderAsync_SnapshotPreservation_SubsequentCatalogPriceChangeDoesNotAffectResult()
    {
        // Arrange
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);

        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);

        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);

        var items = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, new List<ClientSelectedModifierDraft>(), 2000)
        };

        // Act 1: Price calculation
        var result = await engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 2000);
        Assert.Equal(2000, result.LineItems[0].UnitPriceMinorUnits);

        // Act 2: Mutate catalog base price to 5000 in database
        item.UpdateBasePrice(PriceAmount.FromMinorUnits(5000));
        await db.SaveChangesAsync();

        // Assert: Result snapshot remains 2000
        Assert.Equal(2000, result.LineItems[0].UnitPriceMinorUnits);
        Assert.Equal(2000, result.TotalMinorUnits);
    }

    [Fact]
    public async Task PriceOrderAsync_Base4000_Modifier1000_Quantity2_CalculatesAccuratelyWithoutDoubleCounting()
    {
        // Arrange: Tabak taban fiyat 4000, modifier 1000, miktar 2
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment(
            isOrderTakingEnabled: true,
            pricesIncludeTax: true,
            defaultTaxRateBps: 1000);

        var menu = Menu.Create(tenantId, branchId, "Menu", "menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Pizzas", "pizzas", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Pizza", "pizza", PriceAmount.FromMinorUnits(4000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);

        var group = ModifierGroup.Create(tenantId, branchId, "Toppings", 0, 5, sortOrder: 1, isActive: true);
        db.ModifierGroups.Add(group);
        var assign = MenuItemModifierGroupAssignment.Create(tenantId, branchId, menu.Id, item.Id, group.Id, sortOrder: 1);
        db.MenuItemModifierGroupAssignments.Add(assign);

        var opt = ModifierOption.Create(tenantId, branchId, group.Id, "Extra Cheese", PriceAmount.FromMinorUnits(1000), sortOrder: 1, isActive: true);
        db.ModifierOptions.Add(opt);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);
        var items = new List<ClientOrderItemDraft>
        {
            new(
                item.Id,
                null,
                2,
                null,
                new List<ClientSelectedModifierDraft> { new(group.Id.Value, opt.Id.Value) },
                5000) // Effective unit price: 4000 + 1000 = 5000
        };

        // Act
        var result = await engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 10000);

        // Assert
        Assert.Single(result.LineItems);
        var line = result.LineItems[0];
        Assert.Equal(4000, line.UnitPriceMinorUnits);
        Assert.Equal(1000, line.ModifierTotalMinorUnits);
        Assert.Equal(10000, line.LineTotalMinorUnits);
        Assert.Equal(10000, result.TotalMinorUnits);
    }

    [Fact]
    public async Task PriceOrderAsync_WithTaxExcludedAndServiceCharge_CalculatesFullOrderTotalsCorrectly()
    {
        // Arrange: PricesExcludeTax (20%), ServiceCharge (10%)
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment(
            isOrderTakingEnabled: true,
            pricesIncludeTax: false,
            defaultTaxRateBps: 2000,
            serviceChargeRateBps: 1000);

        var menu = Menu.Create(tenantId, branchId, "Menu", "menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Main", "main", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Steak", "steak", PriceAmount.FromMinorUnits(4000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);

        var group = ModifierGroup.Create(tenantId, branchId, "Sauce", 0, 2, 1, true);
        db.ModifierGroups.Add(group);
        db.MenuItemModifierGroupAssignments.Add(MenuItemModifierGroupAssignment.Create(tenantId, branchId, menu.Id, item.Id, group.Id, 1));
        var opt = ModifierOption.Create(tenantId, branchId, group.Id, "Pepper", PriceAmount.FromMinorUnits(1000), 1, true);
        db.ModifierOptions.Add(opt);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);
        var items = new List<ClientOrderItemDraft>
        {
            new(
                item.Id,
                null,
                2,
                null,
                new List<ClientSelectedModifierDraft> { new(group.Id.Value, opt.Id.Value) },
                5000)
        };

        // Act
        var branch = await db.Branches.FirstAsync(b => b.Id == branchId);
        var settings = await db.BranchSettings.FirstOrDefaultAsync(s => s.BranchId == branchId);
        var policy = EffectiveBranchPricingPolicy.Create(branch, settings);
        var result = await engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, policy.PricingPolicyFingerprint, 13200);

        // Net: 10000, Tax (20% on 10000): 2000, Gross: 12000, Service (10% on 12000): 1200, Grand Total: 13200
        Assert.Equal(4000, result.LineItems[0].UnitPriceMinorUnits);
        Assert.Equal(1000, result.LineItems[0].ModifierTotalMinorUnits);
        Assert.Equal(10000, result.LineItems[0].LineTotalMinorUnits);
        Assert.Equal(10000, result.SubtotalMinorUnits);
        Assert.Equal(2000, result.TaxMinorUnits);
        Assert.Equal(1200, result.ServiceChargeMinorUnits);
        Assert.Equal(13200, result.TotalMinorUnits);
    }
}
