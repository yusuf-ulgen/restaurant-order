using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Auth;
using RestaurantOrder.Infrastructure.Orders;
using RestaurantOrder.Infrastructure.Orders.Pricing;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class StaffOrderServiceSupervisorPinTests
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

    private static (UserId managerId, string pin) SeedSupervisor(
        RestaurantOrderDbContext db,
        TenantId tenantId,
        BranchId branchId,
        AuthRole role = AuthRole.BranchManager,
        UserMembershipStatus status = UserMembershipStatus.Active,
        BranchId? membershipBranchId = null)
    {
        var managerId = UserId.New();
        var nowUtc = DateTimeOffset.UtcNow;
        var pin = "1234";
        var hashResult = PinHasher.HashPin(pin);

        var pinCred = PinCredential.Create(
            tenantId,
            managerId,
            branchId,
            hashResult.Hash,
            hashResult.AlgorithmVersion,
            hashResult.PepperKeyId,
            nowUtc);
        db.PinCredentials.Add(pinCred);

        var membership = UserMembership.Create(
            tenantId,
            managerId,
            role,
            membershipBranchId ?? (role == AuthRole.BranchManager ? branchId : null),
            nowUtc,
            initialStatus: status);
        db.Memberships.Add(membership);

        return (managerId, pin);
    }

    [Fact]
    public async Task CancelOrderItemAsync_WaiterWithValidSupervisorPin_SucceedsWasteCancel()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = StaffOrderServiceUnitTests.SeedMenuItem(db, tenantId, branchId);
        var (managerId, pin) = SeedSupervisor(db, tenantId, branchId, AuthRole.BranchManager);
        await db.SaveChangesAsync();

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var waiter = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, "Müşteri özel notu", null, 2000)
        });

        var created = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-sup-valid", waiter);
        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Order.Id));
        order.StartPreparation();
        await db.SaveChangesAsync();

        var cancelRequest = new CancelOrderItemRequest(
            Reason: "Yanlış sipariş hazırlandı",
            SupervisorUserId: managerId.Value,
            SupervisorPin: pin);

        var result = await staffService.CancelOrderItemAsync(
            tenantId,
            branchId,
            sessionId,
            order.Id,
            new OrderItemId(created.Order.Items[0].Id),
            cancelRequest,
            order.ConcurrencyToken,
            waiter);

        Assert.Equal("Cancelled", result.Status);
        var voidAudit = await db.OrderAuditEntries.FirstAsync(a => a.Action == "WASTE_CANCEL");
        Assert.Equal("Yanlış sipariş hazırlandı", voidAudit.Reason);
        Assert.Equal(managerId.Value, voidAudit.ActorId);
        Assert.Equal("BranchManager", voidAudit.ActorRole);
        Assert.DoesNotContain("Müşteri özel notu", voidAudit.DetailsJson);
    }

    [Fact]
    public async Task CancelOrderItemAsync_IncorrectPin_ThrowsForbidden_PersistsFailedAttempt()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = StaffOrderServiceUnitTests.SeedMenuItem(db, tenantId, branchId);
        var (managerId, _) = SeedSupervisor(db, tenantId, branchId, AuthRole.BranchManager);
        await db.SaveChangesAsync();

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var waiter = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
        });

        var created = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-sup-wrong", waiter);
        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Order.Id));
        order.StartPreparation();
        await db.SaveChangesAsync();

        var cancelRequest = new CancelOrderItemRequest(
            Reason: "Zayiat",
            SupervisorUserId: managerId.Value,
            SupervisorPin: "9999"); // Wrong PIN

        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            staffService.CancelOrderItemAsync(
                tenantId,
                branchId,
                sessionId,
                order.Id,
                new OrderItemId(created.Order.Items[0].Id),
                cancelRequest,
                order.ConcurrencyToken,
                waiter));

        Assert.Equal("Supervisor verification failed.", ex.Message);

        // Verification: order and item remain InPreparation
        var refreshedOrder = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderItemStatus.InPreparation, refreshedOrder.Items.First().Status);
        Assert.Equal(OrderStatus.InPreparation, refreshedOrder.Status);

        // Verification: failed attempts counter was persisted in db
        var cred = await db.PinCredentials.FirstAsync(p => p.UserId == managerId);
        Assert.Equal(1, cred.FailedPinAttempts);
    }

    [Fact]
    public async Task CancelOrderItemAsync_FiveWrongAttempts_LocksCredential_SubsequentCorrectPinFails()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = StaffOrderServiceUnitTests.SeedMenuItem(db, tenantId, branchId);
        var (managerId, correctPin) = SeedSupervisor(db, tenantId, branchId, AuthRole.BranchManager);
        await db.SaveChangesAsync();

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var waiter = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
        });

        var created = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-sup-lock", waiter);
        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Order.Id));
        order.StartPreparation();
        await db.SaveChangesAsync();

        var cancelWrong = new CancelOrderItemRequest(
            Reason: "Zayiat",
            SupervisorUserId: managerId.Value,
            SupervisorPin: "0000");

        // 5 consecutive failed attempts
        for (int i = 1; i <= 5; i++)
        {
            await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
                staffService.CancelOrderItemAsync(
                    tenantId,
                    branchId,
                    sessionId,
                    order.Id,
                    new OrderItemId(created.Order.Items[0].Id),
                    cancelWrong,
                    order.ConcurrencyToken,
                    waiter));

            var c = await db.PinCredentials.FirstAsync(p => p.UserId == managerId);
            Assert.Equal(i, c.FailedPinAttempts);
        }

        // Credential must be locked now
        var lockedCred = await db.PinCredentials.FirstAsync(p => p.UserId == managerId);
        Assert.True(lockedCred.IsLocked(DateTimeOffset.UtcNow));

        // 6th attempt with CORRECT PIN must also fail
        var cancelCorrect = new CancelOrderItemRequest(
            Reason: "Zayiat",
            SupervisorUserId: managerId.Value,
            SupervisorPin: correctPin);

        var lockedEx = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            staffService.CancelOrderItemAsync(
                tenantId,
                branchId,
                sessionId,
                order.Id,
                new OrderItemId(created.Order.Items[0].Id),
                cancelCorrect,
                order.ConcurrencyToken,
                waiter));

        Assert.Equal("Supervisor verification failed.", lockedEx.Message);
    }

    [Fact]
    public async Task CancelOrderItemAsync_ManagerFromOtherBranch_IsRejected()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var otherBranchId = BranchId.New();
        var item = StaffOrderServiceUnitTests.SeedMenuItem(db, tenantId, branchId);

        // Manager is assigned to otherBranchId
        var (managerId, pin) = SeedSupervisor(db, tenantId, branchId, AuthRole.BranchManager, membershipBranchId: otherBranchId);
        await db.SaveChangesAsync();

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var waiter = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
        });

        var created = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-sup-other-branch", waiter);
        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Order.Id));
        order.StartPreparation();
        await db.SaveChangesAsync();

        var cancelRequest = new CancelOrderItemRequest(
            Reason: "Zayiat",
            SupervisorUserId: managerId.Value,
            SupervisorPin: pin);

        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            staffService.CancelOrderItemAsync(
                tenantId,
                branchId,
                sessionId,
                order.Id,
                new OrderItemId(created.Order.Items[0].Id),
                cancelRequest,
                order.ConcurrencyToken,
                waiter));

        Assert.Equal("Supervisor verification failed.", ex.Message);
    }

    [Fact]
    public async Task CancelOrderItemAsync_SuspendedManager_IsRejected()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = StaffOrderServiceUnitTests.SeedMenuItem(db, tenantId, branchId);

        // Manager membership is suspended
        var (managerId, pin) = SeedSupervisor(db, tenantId, branchId, AuthRole.BranchManager, status: UserMembershipStatus.Suspended);
        await db.SaveChangesAsync();

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var waiter = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.Waiter);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
        });

        var created = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-sup-suspended", waiter);
        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Order.Id));
        order.StartPreparation();
        await db.SaveChangesAsync();

        var cancelRequest = new CancelOrderItemRequest(
            Reason: "Zayiat",
            SupervisorUserId: managerId.Value,
            SupervisorPin: pin);

        var ex = await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(() =>
            staffService.CancelOrderItemAsync(
                tenantId,
                branchId,
                sessionId,
                order.Id,
                new OrderItemId(created.Order.Items[0].Id),
                cancelRequest,
                order.ConcurrencyToken,
                waiter));

        Assert.Equal("Supervisor verification failed.", ex.Message);
    }

    [Fact]
    public async Task CancelOrderItemAsync_DirectAuthorizedManager_SucceedsWithoutSupervisorPin()
    {
        var (db, tenantId, branchId, sessionId) = OrderPricingTestHelper.CreateTestEnvironment();
        var item = StaffOrderServiceUnitTests.SeedMenuItem(db, tenantId, branchId);

        var pricingEngine = new OrderPricingEngine(db);
        var staffService = new StaffOrderService(db, pricingEngine, PermissionRegistry, PinHasher);
        var manager = CreateStaffPrincipal(tenantId.Value, branchId.Value, AuthRole.BranchManager);

        var request = new CreateStaffOrderRequest(new[]
        {
            new StaffOrderItemRequest(item.Id.Value, null, 1, null, null, 2000)
        });

        var created = await staffService.CreateStaffOrderAsync(tenantId, branchId, sessionId, request, "key-sup-direct", manager);
        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Order.Id));
        order.StartPreparation();
        await db.SaveChangesAsync();

        // No supervisor user or PIN provided; calling actor is directly permitted
        var cancelRequest = new CancelOrderItemRequest("Doğrudan yönetici onayı");

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
        var voidAudit = await db.OrderAuditEntries.FirstAsync(a => a.Action == "WASTE_CANCEL");
        Assert.Equal(manager.SubjectId, voidAudit.ActorId);
    }
}
