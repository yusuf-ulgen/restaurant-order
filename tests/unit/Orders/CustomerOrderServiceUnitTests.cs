using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Orders;
using RestaurantOrder.Infrastructure.Orders.Pricing;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class CustomerOrderServiceUnitTests
{
    private static AuthenticatedPrincipal CreateCustomerPrincipal(Guid tenantId, Guid branchId, Guid sessionId)
    {
        return AuthenticatedPrincipal.CreateCustomer(
            tableSessionId: sessionId,
            tenantId: new TenantId(tenantId),
            branchId: new BranchId(branchId));
    }

    [Fact]
    public async Task CreateOrderAsync_ValidOrder_CreatesSubmittedOrderAndAudit()
    {
        // Arrange
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Cheeseburger", "cheeseburger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var pricingEngine = new OrderPricingEngine(db);
        var orderService = new CustomerOrderService(db, pricingEngine);
        var actor = CreateCustomerPrincipal(tenantId.Value, branchId.Value, sessionId.Value);

        var request = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(item.Id.Value, null, 2, "Az tuzlu lütfen", null, 2000)
        });

        // Act
        var result = await orderService.CreateOrderAsync(
            tenantId,
            branchId,
            sessionId,
            request,
            "idem-key-1",
            actor);

        // Assert
        Assert.False(result.IsReplay);
        Assert.NotNull(result.Order);
        Assert.Equal("Submitted", result.Order.Status);
        Assert.Equal(1, result.Order.RoundNumber);
        Assert.Equal(4000, result.Order.TotalMinorUnits);
        Assert.Single(result.Order.Items);
        Assert.Equal(2, result.Order.Items[0].Quantity);
        Assert.Equal("Az tuzlu lütfen", result.Order.Items[0].CustomerNote);

        // Verify audit entry exists and customer note is not leaked
        var audit = db.OrderAuditEntries.FirstOrDefault(a => a.OrderId.Value == result.Order.Id);
        Assert.NotNull(audit);
        Assert.Equal("OrderSubmitted", audit.Action);
        Assert.DoesNotContain("Az tuzlu lütfen", audit.DetailsJson);
        Assert.Contains("hasNotes\":true", audit.DetailsJson);
    }

    [Fact]
    public async Task CreateOrderAsync_IdenticalKeyAndFingerprint_ReturnsReplay()
    {
        // Arrange
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Cheeseburger", "cheeseburger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var pricingEngine = new OrderPricingEngine(db);
        var orderService = new CustomerOrderService(db, pricingEngine);
        var actor = CreateCustomerPrincipal(tenantId.Value, branchId.Value, sessionId.Value);

        var request = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(item.Id.Value, null, 1, null, null, 2000)
        });

        // Act 1: Initial creation
        var result1 = await orderService.CreateOrderAsync(tenantId, branchId, sessionId, request, "idem-key-replay", actor);
        Assert.False(result1.IsReplay);

        // Act 2: Identical replay with same key
        var result2 = await orderService.CreateOrderAsync(tenantId, branchId, sessionId, request, "idem-key-replay", actor);

        // Assert
        Assert.True(result2.IsReplay);
        Assert.Equal(result1.Order.Id, result2.Order.Id);
        Assert.Equal(result1.Order.RoundNumber, result2.Order.RoundNumber);
        Assert.Equal(1, db.Orders.Count());
    }

    [Fact]
    public async Task CreateOrderAsync_SameKeyDifferentPayload_ThrowsIdempotencyConflictException()
    {
        // Arrange
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Cheeseburger", "cheeseburger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var pricingEngine = new OrderPricingEngine(db);
        var orderService = new CustomerOrderService(db, pricingEngine);
        var actor = CreateCustomerPrincipal(tenantId.Value, branchId.Value, sessionId.Value);

        var request1 = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(item.Id.Value, null, 1, null, null, 2000)
        });

        var request2 = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(item.Id.Value, null, 3, null, null, 2000) // Different quantity
        });

        // Act 1: First order with key
        await orderService.CreateOrderAsync(tenantId, branchId, sessionId, request1, "idem-key-conflict", actor);

        // Act 2 & Assert: Second order with same key but different payload
        var ex = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            orderService.CreateOrderAsync(tenantId, branchId, sessionId, request2, "idem-key-conflict", actor));
        Assert.Equal(IdempotencyConflictException.ErrorCode, IdempotencyConflictException.ErrorCode);
    }

    [Fact]
    public async Task CreateOrderAsync_EmptyOrder_ThrowsDomainException()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var pricingEngine = new OrderPricingEngine(db);
        var orderService = new CustomerOrderService(db, pricingEngine);
        var actor = CreateCustomerPrincipal(tenantId.Value, branchId.Value, sessionId.Value);

        var request = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>());

        await Assert.ThrowsAsync<DomainException>(() =>
            orderService.CreateOrderAsync(tenantId, branchId, sessionId, request, "key-1", actor));
    }

    [Fact]
    public async Task CreateOrderAsync_SessionNotActive_ThrowsSessionNotActive()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment(sessionActive: false);
        var pricingEngine = new OrderPricingEngine(db);
        var orderService = new CustomerOrderService(db, pricingEngine);
        var actor = CreateCustomerPrincipal(tenantId.Value, branchId.Value, sessionId.Value);

        var request = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(Guid.NewGuid(), null, 1, null, null, 2000)
        });

        var ex = await Assert.ThrowsAsync<OrderPricingException>(() =>
            orderService.CreateOrderAsync(tenantId, branchId, sessionId, request, "key-1", actor));
        Assert.Equal(OrderPricingException.CodeSessionNotActive, ex.ErrorCode);
    }

    [Fact]
    public async Task GetOrdersAsync_And_GetOrderByIdAsync_IsolatesToCustomerSession()
    {
        // Arrange
        var (db, tenantId, branchId, session1) = OrderPricingTestHelper.CreateTestEnvironment();
        var session2 = DiningSessionId.New();

        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        var pricingEngine = new OrderPricingEngine(db);
        var orderService = new CustomerOrderService(db, pricingEngine);
        var actor = CreateCustomerPrincipal(tenantId.Value, branchId.Value, session1.Value);

        var request = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(item.Id.Value, null, 1, null, null, 2000)
        });

        var createResult = await orderService.CreateOrderAsync(tenantId, branchId, session1, request, "session1-order", actor);

        // Act 1: Get orders for session1
        var orders = await orderService.GetOrdersAsync(tenantId, branchId, session1);
        Assert.Single(orders);
        Assert.Equal(createResult.Order.Id, orders[0].Id);

        // Act 2: Get orders for session2 (should be empty)
        var orders2 = await orderService.GetOrdersAsync(tenantId, branchId, session2);
        Assert.Empty(orders2);

        // Act 3: GetOrderById for session1 (found)
        var found = await orderService.GetOrderByIdAsync(tenantId, branchId, session1, new OrderId(createResult.Order.Id));
        Assert.NotNull(found);

        // Act 4: GetOrderById for session2 (should return null - no leak)
        var notFound = await orderService.GetOrderByIdAsync(tenantId, branchId, session2, new OrderId(createResult.Order.Id));
        Assert.Null(notFound);
    }
}
