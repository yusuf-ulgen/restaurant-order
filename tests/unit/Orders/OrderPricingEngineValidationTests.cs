using RestaurantOrder.Application.Orders.Pricing;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Infrastructure.Orders.Pricing;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class OrderPricingEngineValidationTests
{
    [Fact]
    public async Task PriceOrderAsync_WhenSessionNotActive_ThrowsSessionNotActive()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment(sessionActive: false);

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.Menus.Add(menu);
        db.MenuCategories.Add(cat);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);
        var items = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, null, 2000)
        };

        var ex = await Assert.ThrowsAsync<OrderPricingException>(() => engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 2000));
        Assert.Equal(OrderPricingException.CodeSessionNotActive, ex.ErrorCode);
    }

    [Fact]
    public async Task PriceOrderAsync_WhenOrderTakingDisabled_ThrowsOrderTakingDisabled()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment(isOrderTakingEnabled: false);

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.Menus.Add(menu);
        db.MenuCategories.Add(cat);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);
        var items = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, null, 2000)
        };

        var ex = await Assert.ThrowsAsync<OrderPricingException>(() => engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 2000));
        Assert.Equal(OrderPricingException.CodeOrderTakingDisabled, ex.ErrorCode);
    }

    [Fact]
    public async Task PriceOrderAsync_WhenItemIs86Stockout_ThrowsItemOutOfStock()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.Menus.Add(menu);
        db.MenuCategories.Add(cat);
        db.MenuItems.Add(item);

        var avail = BranchItemAvailability.CreateItemUnavailable(
            tenantId,
            branchId,
            item.Id,
            AvailabilityReasonCode.SoldOut,
            "Out of stock",
            null,
            UserId.New());
        db.BranchItemAvailabilities.Add(avail);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);
        var items = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, new List<ClientSelectedModifierDraft>(), 2000)
        };

        var ex = await Assert.ThrowsAsync<OrderPricingException>(() => engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 2000));
        Assert.Equal(OrderPricingException.CodeItemOutOfStock, ex.ErrorCode);
    }

    [Fact]
    public async Task PriceOrderAsync_WhenVariantBelongsToDifferentItem_ThrowsInvalidVariant()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        var item1 = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger 1", "burger-1", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        var item2 = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger 2", "burger-2", PriceAmount.FromMinorUnits(2500), sortOrder: 2, isActive: true);
        db.Menus.Add(menu);
        db.MenuCategories.Add(cat);
        db.MenuItems.AddRange(item1, item2);

        var variant2 = ItemVariant.Create(tenantId, branchId, menu.Id, item2.Id, "Extra Patty", "XTRA", PriceAmount.FromMinorUnits(500), sortOrder: 1, isActive: true);
        db.ItemVariants.Add(variant2);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);
        var items = new List<ClientOrderItemDraft>
        {
            new(item1.Id, variant2.Id, 1, null, new List<ClientSelectedModifierDraft>(), 2500)
        };

        var ex = await Assert.ThrowsAsync<OrderPricingException>(() => engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 2500));
        Assert.Equal(OrderPricingException.CodeInvalidVariant, ex.ErrorCode);
    }

    [Fact]
    public async Task PriceOrderAsync_WhenDuplicateModifierSelected_ThrowsDuplicateModifier()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.Menus.Add(menu);
        db.MenuCategories.Add(cat);
        db.MenuItems.Add(item);

        var group = ModifierGroup.Create(tenantId, branchId, "Cheese", 0, 5, sortOrder: 1, isActive: true);
        db.ModifierGroups.Add(group);

        var assign = MenuItemModifierGroupAssignment.Create(tenantId, branchId, menu.Id, item.Id, group.Id, sortOrder: 1);
        db.MenuItemModifierGroupAssignments.Add(assign);

        var optCheddar = ModifierOption.Create(tenantId, branchId, group.Id, "Cheddar", PriceAmount.FromMinorUnits(300), sortOrder: 1, isActive: true);
        db.ModifierOptions.Add(optCheddar);
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
                    new(group.Id.Value, optCheddar.Id.Value)
                },
                2600)
        };

        var ex = await Assert.ThrowsAsync<OrderPricingException>(() => engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 2600));
        Assert.Equal(OrderPricingException.CodeDuplicateModifier, ex.ErrorCode);
    }

    [Fact]
    public async Task PriceOrderAsync_WhenModifierGroupMinMaxViolated_ThrowsInvalidModifierSelection()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.Menus.Add(menu);
        db.MenuCategories.Add(cat);
        db.MenuItems.Add(item);

        var group = ModifierGroup.Create(tenantId, branchId, "Required Sauce", 1, 1, sortOrder: 1, isActive: true);
        db.ModifierGroups.Add(group);

        var assign = MenuItemModifierGroupAssignment.Create(tenantId, branchId, menu.Id, item.Id, group.Id, sortOrder: 1);
        db.MenuItemModifierGroupAssignments.Add(assign);

        var optSauce = ModifierOption.Create(tenantId, branchId, group.Id, "Mayo", PriceAmount.FromMinorUnits(0), sortOrder: 1, isActive: true);
        db.ModifierOptions.Add(optSauce);
        await db.SaveChangesAsync();

        var engine = new OrderPricingEngine(db);
        var items = new List<ClientOrderItemDraft>
        {
            new(item.Id, null, 1, null, new List<ClientSelectedModifierDraft>(), 2000)
        };

        var ex = await Assert.ThrowsAsync<OrderPricingException>(() => engine.PriceAndValidateOrderAsync(
            tenantId, branchId, sessionId, items, OrderPricingTestHelper.DefaultTestPolicyFingerprint, 2000));
        Assert.Equal(OrderPricingException.CodeInvalidModifierSelection, ex.ErrorCode);
    }
}
