using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Auth;
using RestaurantOrder.Infrastructure.Orders;
using RestaurantOrder.Infrastructure.Orders.Pricing;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class StaffOrderServiceUnitTests
{
    private static readonly IPermissionRegistry PermissionRegistry = new PermissionRegistry();
    private static readonly Mock<ISupervisorPinService> SupervisorPinServiceMock = new();
    private static readonly IPinHasher PinHasher = new PepperedPinHasher(
        Options.Create(new PinHasherOptions
        {
            PepperKeyId = "test-pepper-1",
            PepperValue = "0123456789abcdef0123456789abcdef",
            Environment = "test"
        }));

    private static AuthenticatedPrincipal CreateStaffPrincipal(
        Guid tenantId,
        Guid branchId,
        AuthRole role = AuthRole.Waiter)
    {
        var userId = Guid.NewGuid();
        return AuthenticatedPrincipal.CreateStaff(
            userId,
            role,
            AuthorizationScope.ForBranch(new TenantId(tenantId), new BranchId(branchId)),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Pin,
            securityVersion: 1);
    }

    [Fact]
    public async Task CreateStaffOrderAsync_ValidRequest_CreatesOrderWithStaffSourceAndMaskedNotesInAudit()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(
            new[]
            {
                new StaffOrderItemRequest(item.Id.Value, null, 2, "Hassas Müşteri Notu", null, 2000)
            },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            4000);

        var result = await staffService.CreateStaffOrderAsync(
            tenantId,
            branchId,
            sessionId,
            request,
            "staff-idem-1",
            actor);

        Assert.False(result.IsReplay);
        Assert.Equal("Staff", result.Order.OrderSource);
        Assert.Equal("Submitted", result.Order.Status);
        Assert.Single(result.Order.Items);

        // Verify audit log exists and does NOT contain customer note
        var audit = Assert.Single(db.OrderAuditEntries);
        Assert.Equal("StaffOrderCreated", audit.Action);
        Assert.DoesNotContain("Hassas Müşteri Notu", audit.DetailsJson);
        Assert.Contains("\"hasNotes\":true", audit.DetailsJson);
    }

    [Fact]
    public async Task CreateStaffOrderAsync_CustomerPrincipal_ThrowsInvalidAuthorizationScopeException()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var customerActor = AuthenticatedPrincipal.CreateCustomer(sessionId.Value, tenantId, branchId);

        var request = new CreateStaffOrderRequest(
            new[]
            {
                new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
            },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            2000);

        await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-c", customerActor));
    }

    [Fact]
    public async Task CreateStaffOrderAsync_MismatchedBranch_ThrowsInvalidAuthorizationScopeException()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var otherBranchId = BranchId.New();
        var item = SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var actor = CreateStaffPrincipal(tenantId.Value, otherBranchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(
            new[]
            {
                new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
            },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            2000);

        await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-diff-b", actor));
    }

    [Fact]
    public async Task CancelOrderItemAsync_SubmittedItem_CancelledByWaiter_Succeeds()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(
            new[]
            {
                new StaffOrderItemRequest(item.Id.Value, null, 1, "Notum", null, 2000)
            },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            2000);

        var created = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-cancel-1", actor);
        var orderItemId = created.Order.Items[0].Id;

        var cancelRequest = new CancelOrderItemRequest("Müşteri başka ürün istedi");
        var updated = await staffService.CancelOrderItemAsync(
            tenantId,
            branchId,
            sessionId,
            new OrderId(created.Order.Id),
            new OrderItemId(orderItemId),
            cancelRequest,
            created.Order.ConcurrencyToken,
            actor);

        Assert.Equal("Cancelled", updated.Status);
        Assert.Equal("Cancelled", updated.Items[0].Status);

        var cancelAudit = db.OrderAuditEntries.First(a => a.Action == "ItemCancelled");
        Assert.Equal("Müşteri başka ürün istedi", cancelAudit.Reason);
        Assert.DoesNotContain("Notum", cancelAudit.DetailsJson);
    }

    [Fact]
    public async Task CancelOrderItemAsync_InPreparationItem_WithoutSupervisorAuth_ThrowsForbidden()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(
            new[]
            {
                new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
            },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            2000);

        var created = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-prep-1", actor);
        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Order.Id));
        order.StartPreparation();
        await db.SaveChangesAsync();

        var cancelRequest = new CancelOrderItemRequest("Müşteri beklemek istemedi");
        await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            staffService.CancelOrderItemAsync(
                tenantId,
                branchId,
                sessionId,
                order.Id,
                new OrderItemId(created.Order.Items[0].Id),
                cancelRequest,
                order.ConcurrencyToken,
                actor));
    }

    [Fact]
    public async Task CancelOrderItemAsync_InPreparationItem_SupervisorVoid_Succeeds()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var waiter = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);
        var manager = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.BranchManager);

        var request = new CreateStaffOrderRequest(
            new[]
            {
                new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
            },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            2000);

        var created = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-sup-1", waiter);
        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Order.Id));
        order.StartPreparation();
        await db.SaveChangesAsync();

        var cancelRequest = new CancelOrderItemRequest("Zayiat olarak onaylandı");
        var result = await staffService.CancelOrderItemAsync(
            tenantId,
            branchId,
            sessionId,
            order.Id,
            new OrderItemId(created.Order.Items[0].Id),
            cancelRequest,
            order.ConcurrencyToken,
            manager);

        Assert.Equal("Cancelled", result.Status);
        var voidAudit = db.OrderAuditEntries.First(a => a.Action == "WASTE_CANCEL");
        Assert.Equal("Zayiat olarak onaylandı", voidAudit.Reason);
        Assert.Equal(manager.SubjectId, voidAudit.ActorId);
    }

    [Fact]
    public async Task CancelOrderItemAsync_StaleConcurrencyToken_ThrowsConflict()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(
            new[]
            {
                new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
            },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            2000);

        var created = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-concur-1", actor);

        var cancelRequest = new CancelOrderItemRequest("İptal");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            staffService.CancelOrderItemAsync(
                tenantId,
                branchId,
                sessionId,
                new OrderId(created.Order.Id),
                new OrderItemId(created.Order.Items[0].Id),
                cancelRequest,
                Guid.NewGuid(), // Stale token
                actor));
    }

    [Fact]
    public async Task CreateStaffOrderAsync_OnlyNoteChanged_ThrowsIdempotencyConflict()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var req1 = new CreateStaffOrderRequest(
            new[] { new StaffOrderItemRequest(item.Id.Value, null, 1, "Not 1", null, 2000) },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            2000);
        var req2 = new CreateStaffOrderRequest(
            new[] { new StaffOrderItemRequest(item.Id.Value, null, 1, "Not 2", null, 2000) },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            2000);

        await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, req1, "staff-key-1", actor);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, req2, "staff-key-1", actor));
    }

    [Fact]
    public async Task CreateStaffOrderAsync_OnlyExpectedPriceChanged_ThrowsIdempotencyConflict()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var req1 = new CreateStaffOrderRequest(
            new[] { new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000) },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            2000);
        var req2 = new CreateStaffOrderRequest(
            new[] { new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2500) },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            2500);

        await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, req1, "staff-key-price", actor);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, req2, "staff-key-price", actor));
    }

    [Fact]
    public async Task CreateStaffOrderAsync_ItemOrderReversed_ReturnsReplay()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item1 = SeedMenuItem(db, tenantId, branchId);
        var item2 = MenuItem.Create(tenantId, branchId, item1.MenuId, item1.CategoryId, "Calzone", "calzone", PriceAmount.FromMinorUnits(3000), sortOrder: 2, isActive: true);
        db.MenuItems.Add(item2);
        db.SaveChanges();

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var req1 = new CreateStaffOrderRequest(
            new[]
            {
                new StaffOrderItemRequest(item1.Id.Value, null, 1, null, null, 2000),
                new StaffOrderItemRequest(item2.Id.Value, null, 1, null, null, 3000)
            },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            5000);

        var req2 = new CreateStaffOrderRequest(
            new[]
            {
                new StaffOrderItemRequest(item2.Id.Value, null, 1, null, null, 3000),
                new StaffOrderItemRequest(item1.Id.Value, null, 1, null, null, 2000)
            },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            5000);

        var res1 = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, req1, "staff-key-order", actor);
        var res2 = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, req2, "staff-key-order", actor);

        Assert.False(res1.IsReplay);
        Assert.True(res2.IsReplay);
        Assert.Equal(res1.Order.Id, res2.Order.Id);
    }

    [Fact]
    public async Task CreateStaffOrderAsync_Base4000_Modifier1000_Quantity2_YieldsExpectedSnapshotTotals()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var menu = Menu.Create(tenantId, branchId, "Menu", "menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Burgers", "burgers", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Burger", "burger", PriceAmount.FromMinorUnits(4000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);

        var group = ModifierGroup.Create(tenantId, branchId, "Extra", 0, 2, sortOrder: 1, isActive: true);
        db.ModifierGroups.Add(group);
        db.MenuItemModifierGroupAssignments.Add(MenuItemModifierGroupAssignment.Create(tenantId, branchId, menu.Id, item.Id, group.Id, sortOrder: 1));
        var opt = ModifierOption.Create(tenantId, branchId, group.Id, "Cheese", PriceAmount.FromMinorUnits(1000), sortOrder: 1, isActive: true);
        db.ModifierOptions.Add(opt);
        db.SaveChanges();

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, SupervisorPinServiceMock.Object);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(
            new[]
            {
                new StaffOrderItemRequest(
                    item.Id.Value,
                    null,
                    2,
                    null,
                    new[] { new StaffSelectedModifierRequest(group.Id.Value, opt.Id.Value) },
                    5000)
            },
            OrderPricingTestHelper.DefaultTestPolicyFingerprint,
            10000);

        var result = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-mod-snap", actor);

        Assert.Equal(10000, result.Order.TotalMinorUnits);
        Assert.Single(result.Order.Items);
        var itemDto = result.Order.Items[0];
        Assert.Equal(4000, itemDto.UnitPriceMinorUnits);
        Assert.Equal(1000, itemDto.ModifierTotalMinorUnits);
        Assert.Equal(10000, itemDto.LineTotalMinorUnits);
        Assert.Equal(2, itemDto.Quantity);
    }

    internal static MenuItem SeedMenuItem(RestaurantOrderDbContext db, TenantId tenantId, BranchId branchId)
    {
        var menu = Menu.Create(tenantId, branchId, "Main Menu", "main-menu", sortOrder: 1, status: MenuStatus.Active);
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tenantId, branchId, menu.Id, "Pizzas", "pizzas", sortOrder: 1, isActive: true);
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(tenantId, branchId, menu.Id, cat.Id, "Margherita", "margherita", PriceAmount.FromMinorUnits(2000), sortOrder: 1, isActive: true);
        db.MenuItems.Add(item);
        db.SaveChanges();
        return item;
    }
}
