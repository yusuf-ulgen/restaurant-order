using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

using static RestaurantOrder.UnitTests.Notifications.DeviceRegistrationTestHelpers;

namespace RestaurantOrder.UnitTests.Notifications;

public class DeviceRegistrationEndpointsTests
{

    [Fact]
    public async Task RegisterDevice_CustomerWithActiveSession_CreatesRegistration()
    {
        using var db = CreateInMemoryDbContext();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var principal = CreateCustomerPrincipal(tenantId, branchId, sessionId);
        var request = new RegisterDeviceRequest("inst_cust_12345", "web");

        var result = await SimulateRegisterDevice(request, principal, sessionValidatorMock.Object, db);

        var okResult = Assert.IsType<Ok<DeviceRegistrationResponse>>(result);
        var response = okResult.Value;
        Assert.NotNull(response);
        Assert.Equal("inst_cust_12345", response.InstallationId);
        Assert.Equal("web", response.Platform);

        var stored = await db.DeviceRegistrations.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.InstallationId == "inst_cust_12345");
        Assert.NotNull(stored);
        Assert.Equal(new DiningSessionId(sessionId), stored.DiningSessionId);
        Assert.Null(stored.UserId);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task RegisterDevice_SameCustomerOwner_RenewsRegistration()
    {
        using var db = CreateInMemoryDbContext();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var initialExpiry = DateTimeOffset.UtcNow.AddMinutes(10);
        var existingReg = DeviceRegistration.CreateForCustomer(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionId),
            "inst_same_owner",
            "web",
            DateTimeOffset.UtcNow.AddHours(-1),
            TimeSpan.FromHours(1));

        db.DeviceRegistrations.Add(existingReg);
        await db.SaveChangesAsync();

        var principal = CreateCustomerPrincipal(tenantId, branchId, sessionId);
        var request = new RegisterDeviceRequest("inst_same_owner", "web");

        var result = await SimulateRegisterDevice(request, principal, sessionValidatorMock.Object, db);

        var okResult = Assert.IsType<Ok<DeviceRegistrationResponse>>(result);
        Assert.NotNull(okResult.Value);

        var stored = await db.DeviceRegistrations.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.InstallationId == "inst_same_owner");
        Assert.NotNull(stored);
        Assert.True(stored.ExpiresAtUtc > initialExpiry);
        Assert.Equal(new DiningSessionId(sessionId), stored.DiningSessionId);
    }

    [Fact]
    public async Task RegisterDevice_DifferentActiveCustomerSession_ReturnsConflict()
    {
        using var db = CreateInMemoryDbContext();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();

        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var existingReg = DeviceRegistration.CreateForCustomer(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionA),
            "inst_shared_tablet",
            "web",
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1));

        db.DeviceRegistrations.Add(existingReg);
        await db.SaveChangesAsync();

        // Session B attempts to register the same device while Session A is active
        var principalB = CreateCustomerPrincipal(tenantId, branchId, sessionB);
        var request = new RegisterDeviceRequest("inst_shared_tablet", "web");

        var result = await SimulateRegisterDevice(request, principalB, sessionValidatorMock.Object, db);

        var conflict = Assert.IsType<Conflict<ProblemDetails>>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);

        // Verify session A registration remains untouched
        var stored = await db.DeviceRegistrations.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.InstallationId == "inst_shared_tablet");
        Assert.NotNull(stored);
        Assert.Equal(new DiningSessionId(sessionA), stored.DiningSessionId);
    }

    [Fact]
    public async Task RegisterDevice_DifferentCustomerSession_WhenOldSessionClosed_RebindsToNewSession()
    {
        using var db = CreateInMemoryDbContext();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();

        // Old session A is closed
        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // New session B is active
        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var existingReg = DeviceRegistration.CreateForCustomer(
            new TenantId(tenantId),
            new BranchId(branchId),
            new DiningSessionId(sessionA),
            "inst_tablet_table_switch",
            "web",
            DateTimeOffset.UtcNow.AddMinutes(-30),
            TimeSpan.FromHours(1));

        db.DeviceRegistrations.Add(existingReg);
        await db.SaveChangesAsync();

        // Session B (table B) claims the tablet after session A has closed
        var principalB = CreateCustomerPrincipal(tenantId, branchId, sessionB);
        var request = new RegisterDeviceRequest("inst_tablet_table_switch", "web");

        var result = await SimulateRegisterDevice(request, principalB, sessionValidatorMock.Object, db);

        var okResult = Assert.IsType<Ok<DeviceRegistrationResponse>>(result);
        Assert.NotNull(okResult.Value);

        var stored = await db.DeviceRegistrations.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.InstallationId == "inst_tablet_table_switch");
        Assert.NotNull(stored);
        Assert.Equal(new DiningSessionId(sessionB), stored.DiningSessionId);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task RegisterDevice_CrossBranch_WhenActive_ReturnsConflict()
    {
        using var db = CreateInMemoryDbContext();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var existingReg = DeviceRegistration.CreateForStaff(
            new TenantId(tenantId),
            new BranchId(branchA),
            new UserId(userId),
            "inst_cross_branch",
            "android",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(30));

        db.DeviceRegistrations.Add(existingReg);
        await db.SaveChangesAsync();

        // Caller from Branch B attempts to register device active in Branch A
        var principalBranchB = CreateStaffPrincipal(tenantId, branchB, userId);
        var request = new RegisterDeviceRequest("inst_cross_branch", "android");

        var result = await SimulateRegisterDevice(request, principalBranchB, sessionValidatorMock.Object, db);

        var conflict = Assert.IsType<Conflict<ProblemDetails>>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_CustomerMissingSessionClaim_ReturnsUnauthorized()
    {
        using var db = CreateInMemoryDbContext();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        // Customer principal without table_session_id claim
        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(JwtClaimNames.PrincipalType, "customer")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var request = new RegisterDeviceRequest("inst_missing_claim", "web");

        var result = await SimulateRegisterDevice(request, principal, sessionValidatorMock.Object, db);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task RegisterDevice_StaffMissingSubjectClaim_ReturnsUnauthorized()
    {
        using var db = CreateInMemoryDbContext();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        // Staff principal without sub claim
        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Waiter"),
            new Claim(JwtClaimNames.PrincipalType, "staff")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var request = new RegisterDeviceRequest("inst_missing_sub", "android");

        var result = await SimulateRegisterDevice(request, principal, sessionValidatorMock.Object, db);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task UnregisterDevice_CallerMatchesOwner_DeactivatesRegistration()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var reg = DeviceRegistration.CreateForStaff(
            new TenantId(tenantId),
            new BranchId(branchId),
            new UserId(userId),
            "inst_staff_to_delete",
            "ios",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(30));

        db.DeviceRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var principal = CreateStaffPrincipal(tenantId, branchId, userId);
        var result = await SimulateUnregisterDevice("inst_staff_to_delete", principal, db);

        Assert.IsType<NoContent>(result);

        var updated = await db.DeviceRegistrations.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.InstallationId == "inst_staff_to_delete");
        Assert.NotNull(updated);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task UnregisterDevice_DifferentOwner_DoesNotDeactivateOtherUserDevice()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var userOwner = Guid.NewGuid();
        var userAttacker = Guid.NewGuid();

        var reg = DeviceRegistration.CreateForStaff(
            new TenantId(tenantId),
            new BranchId(branchId),
            new UserId(userOwner),
            "inst_protected_device",
            "ios",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(30));

        db.DeviceRegistrations.Add(reg);
        await db.SaveChangesAsync();

        // Attacker attempts to delete userOwner's device
        var principalAttacker = CreateStaffPrincipal(tenantId, branchId, userAttacker);
        var result = await SimulateUnregisterDevice("inst_protected_device", principalAttacker, db);

        // Idempotent 204 returned without deactivating target
        Assert.IsType<NoContent>(result);

        var stored = await db.DeviceRegistrations.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.InstallationId == "inst_protected_device");
        Assert.NotNull(stored);
        Assert.True(stored.IsActive);
    }
}
