using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using Xunit;
using static RestaurantOrder.UnitTests.Notifications.DeviceRegistrationTestHelpers;

namespace RestaurantOrder.UnitTests.Notifications;

public class DeviceRegistrationAuthorizationMatrixTests
{
    [Theory]
    [InlineData(false, "tenant_id")]
    [InlineData(false, "branch_id")]
    [InlineData(false, "sub")]
    [InlineData(true, "tenant_id")]
    [InlineData(true, "branch_id")]
    [InlineData(true, "table_session_id")]
    public async Task MissingScope_RejectsRegistrationAndUnregistrationWithoutMutation(bool customer, string missing)
    {
        await using var db = CreateInMemoryDbContext();
        var t = Guid.NewGuid(); var b = Guid.NewGuid(); var owner = Guid.NewGuid();
        var principal = customer ? CreateCustomerPrincipal(t, b, owner) : CreateStaffPrincipal(t, b, owner);
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.RemoveClaim(identity.FindFirst(missing)!);
        var registering = await SimulateRegisterDevice(new RegisterDeviceRequest("synthetic", "web"), principal, Mock.Of<ICustomerSessionValidator>(), db);
        Assert.IsType<UnauthorizedHttpResult>(registering);
        if (missing != "branch_id") Assert.IsType<UnauthorizedHttpResult>(await SimulateUnregisterDevice("synthetic", principal, db));
        Assert.Empty(await db.DeviceRegistrations.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task InactiveCustomerSession_CannotRegisterDevice()
    {
        await using var db = CreateInMemoryDbContext();
        var principal = CreateCustomerPrincipal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.IsType<ForbidHttpResult>(await SimulateRegisterDevice(new("synthetic", "web"), principal, Mock.Of<ICustomerSessionValidator>(), db));
        Assert.Empty(await db.DeviceRegistrations.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StaffRegistration_OnlyRebindsForeignDeviceWhenInactive(bool previousCustomer, bool active)
    {
        await using var db = CreateInMemoryDbContext();
        var t = new TenantId(Guid.NewGuid()); var b = new BranchId(Guid.NewGuid());
        var owner = new UserId(Guid.NewGuid()); var old = new UserId(Guid.NewGuid()); var now = DateTimeOffset.UtcNow;
        var device = previousCustomer
            ? DeviceRegistration.CreateForCustomer(t, b, new DiningSessionId(Guid.NewGuid()), "synthetic", "web", now, TimeSpan.FromDays(1))
            : DeviceRegistration.CreateForStaff(t, b, old, "synthetic", "web", now, TimeSpan.FromDays(1));
        if (!active) device.Deactivate(now);
        db.DeviceRegistrations.Add(device); await db.SaveChangesAsync();
        var principal = CreateStaffPrincipal(t.Value, b.Value, owner.Value);
        var result = await SimulateRegisterDevice(new("synthetic", "web"), principal, Mock.Of<ICustomerSessionValidator>(), db);
        if (active)
        {
            Assert.IsType<Conflict<ProblemDetails>>(result);
            Assert.NotEqual(owner, device.UserId);
        }
        else
        {
            Assert.IsType<Ok<DeviceRegistrationResponse>>(result);
            Assert.Equal(owner, device.UserId); Assert.Null(device.DiningSessionId);
            Assert.IsType<NoContent>(await SimulateUnregisterDevice("synthetic", principal, db));
            Assert.False(device.IsActive);
            Assert.IsType<NoContent>(await SimulateUnregisterDevice("synthetic", principal, db));
        }
    }

    [Fact]
    public async Task SameStaffOwner_RenewsWithoutChangingIdentity()
    {
        await using var db = CreateInMemoryDbContext();
        var t = Guid.NewGuid(); var b = Guid.NewGuid(); var owner = Guid.NewGuid();
        var principal = CreateStaffPrincipal(t, b, owner);
        var validator = Mock.Of<ICustomerSessionValidator>();
        await SimulateRegisterDevice(new("synthetic", "web"), principal, validator, db);
        var first = await db.DeviceRegistrations.IgnoreQueryFilters().SingleAsync();
        await SimulateRegisterDevice(new("synthetic", "web"), principal, validator, db);
        Assert.Equal(first.Id, (await db.DeviceRegistrations.IgnoreQueryFilters().SingleAsync()).Id);
    }
}
