using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 2, "Hassas Müşteri Notu", null, 2000)
        });

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
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var customerActor = AuthenticatedPrincipal.CreateCustomer(sessionId.Value, tenantId, branchId);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
        });

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
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var actor = CreateStaffPrincipal(tenantId.Value, otherBranchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
        });

        await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-diff-b", actor));
    }

    [Fact]
    public async Task CancelOrderItemAsync_SubmittedItem_CancelledByWaiter_Succeeds()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, "Notum", null, 2000)
        });

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
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
        });

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
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var waiter = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);
        var manager = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.BranchManager);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
        });

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
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var actor = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
        });

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

    private static MenuItem SeedMenuItem(RestaurantOrderDbContext db, TenantId tenantId, BranchId branchId)
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
