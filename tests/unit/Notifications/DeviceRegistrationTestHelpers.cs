using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.UnitTests.Notifications;

public static class DeviceRegistrationTestHelpers
{
    public static RestaurantOrderDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(databaseName: $"dev_reg_test_{Guid.NewGuid():N}")
            .Options;

        return new RestaurantOrderDbContext(options);
    }

    public static ClaimsPrincipal CreateCustomerPrincipal(Guid tenantId, Guid branchId, Guid sessionId)
    {
        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(JwtClaimNames.PrincipalType, "customer"),
            new Claim(JwtClaimNames.TableSessionId, sessionId.ToString())
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    public static ClaimsPrincipal CreateStaffPrincipal(Guid tenantId, Guid branchId, Guid userId)
    {
        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Waiter"),
            new Claim(JwtClaimNames.PrincipalType, "staff"),
            new Claim(JwtClaimNames.Subject, userId.ToString())
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    public static Task<IResult> SimulateRegisterDevice(
        RegisterDeviceRequest request,
        ClaimsPrincipal user,
        ICustomerSessionValidator sessionValidator,
        RestaurantOrderDbContext dbContext)
    {
        return RestaurantOrder.Api.Realtime.DeviceRegistrationEndpoints.RegisterDeviceHandlerAsync(
            request,
            user,
            sessionValidator,
            dbContext);
    }

    public static Task<IResult> SimulateUnregisterDevice(
        string installationId,
        ClaimsPrincipal user,
        RestaurantOrderDbContext dbContext)
    {
        return RestaurantOrder.Api.Realtime.DeviceRegistrationEndpoints.UnregisterDeviceHandlerAsync(
            installationId,
            user,
            dbContext);
    }
}
