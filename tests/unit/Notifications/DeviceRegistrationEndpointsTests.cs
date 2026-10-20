using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Notifications;

public class DeviceRegistrationEndpointsTests
{
    private static RestaurantOrderDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(databaseName: $"dev_reg_test_{Guid.NewGuid():N}")
            .Options;

        return new RestaurantOrderDbContext(options);
    }

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

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(JwtClaimNames.PrincipalType, "customer"),
            new Claim(JwtClaimNames.TableSessionId, sessionId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var request = new RegisterDeviceRequest("inst_cust_12345", "web");

        // Simulate endpoint logic
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
    public async Task RegisterDevice_CustomerWithInactiveSession_ReturnsForbid()
    {
        using var db = CreateInMemoryDbContext();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(JwtClaimNames.PrincipalType, "customer"),
            new Claim(JwtClaimNames.TableSessionId, sessionId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var request = new RegisterDeviceRequest("inst_cust_fail", "web");

        var result = await SimulateRegisterDevice(request, principal, sessionValidatorMock.Object, db);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task RegisterDevice_StaffUser_CreatesRegistrationBoundToUserId()
    {
        using var db = CreateInMemoryDbContext();
        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Waiter"),
            new Claim(JwtClaimNames.PrincipalType, "staff"),
            new Claim(JwtClaimNames.Subject, userId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var request = new RegisterDeviceRequest("inst_waiter_999", "android");

        var result = await SimulateRegisterDevice(request, principal, sessionValidatorMock.Object, db);

        var okResult = Assert.IsType<Ok<DeviceRegistrationResponse>>(result);
        var response = okResult.Value;
        Assert.NotNull(response);
        Assert.Equal("inst_waiter_999", response.InstallationId);

        var stored = await db.DeviceRegistrations.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.InstallationId == "inst_waiter_999");
        Assert.NotNull(stored);
        Assert.Equal(new UserId(userId), stored.UserId);
        Assert.Null(stored.DiningSessionId);
    }

    [Fact]
    public async Task UnregisterDevice_DeactivatesCallerDevice()
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

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.Subject, userId.ToString()),
            new Claim(ClaimTypes.Role, "Waiter"),
            new Claim(JwtClaimNames.PrincipalType, "staff")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var result = await SimulateUnregisterDevice("inst_staff_to_delete", principal, db);

        Assert.IsType<NoContent>(result);

        var updated = await db.DeviceRegistrations.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.InstallationId == "inst_staff_to_delete");
        Assert.NotNull(updated);
        Assert.False(updated.IsActive);
    }

    private static async Task<IResult> SimulateRegisterDevice(
        RegisterDeviceRequest request,
        ClaimsPrincipal user,
        ICustomerSessionValidator sessionValidator,
        RestaurantOrderDbContext dbContext)
    {
        var tenantClaim = user.FindFirst(JwtClaimNames.TenantId)?.Value ?? user.FindFirst("tenant_id")?.Value;
        var branchClaim = user.FindFirst(JwtClaimNames.BranchId)?.Value ?? user.FindFirst("branch_id")?.Value;
        var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
        var principalType = user.FindFirst(JwtClaimNames.PrincipalType)?.Value ?? user.FindFirst("principal_type")?.Value;

        if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantId == Guid.Empty ||
            !Guid.TryParse(branchClaim, out var branchId) || branchId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var tenantIdTyped = new TenantId(tenantId);
        var branchIdTyped = new BranchId(branchId);
        var isCustomer = string.Equals(principalType, "customer", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(roleClaim, "Customer", StringComparison.OrdinalIgnoreCase);

        DeviceRegistration registration;

        if (isCustomer)
        {
            var tableSessionClaim = user.FindFirst(JwtClaimNames.TableSessionId)?.Value ?? user.FindFirst("table_session_id")?.Value;
            if (!Guid.TryParse(tableSessionClaim, out var sessionId) || sessionId == Guid.Empty)
            {
                return Results.Unauthorized();
            }

            var isSessionActive = await sessionValidator.ValidateSessionActiveAsync(tenantId, branchId, sessionId);
            if (!isSessionActive)
            {
                return Results.Forbid();
            }

            var sessionTyped = new DiningSessionId(sessionId);
            var existing = await dbContext.DeviceRegistrations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.TenantId == tenantIdTyped && d.InstallationId == request.InstallationId);

            if (existing != null)
            {
                existing.Renew(now.AddDays(1), now);
                registration = existing;
            }
            else
            {
                registration = DeviceRegistration.CreateForCustomer(
                    tenantIdTyped,
                    branchIdTyped,
                    sessionTyped,
                    request.InstallationId,
                    request.Platform,
                    now,
                    TimeSpan.FromDays(1));
                dbContext.DeviceRegistrations.Add(registration);
            }
        }
        else
        {
            var subClaim = user.FindFirst(JwtClaimNames.Subject)?.Value ?? user.FindFirst("sub")?.Value;
            if (!Guid.TryParse(subClaim, out var staffUserId) || staffUserId == Guid.Empty)
            {
                return Results.Unauthorized();
            }

            var userIdTyped = new UserId(staffUserId);
            var existing = await dbContext.DeviceRegistrations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.TenantId == tenantIdTyped && d.InstallationId == request.InstallationId);

            if (existing != null)
            {
                existing.Renew(now.AddDays(30), now);
                registration = existing;
            }
            else
            {
                registration = DeviceRegistration.CreateForStaff(
                    tenantIdTyped,
                    branchIdTyped,
                    userIdTyped,
                    request.InstallationId,
                    request.Platform,
                    now,
                    TimeSpan.FromDays(30));
                dbContext.DeviceRegistrations.Add(registration);
            }
        }

        await dbContext.SaveChangesAsync();

        return Results.Ok(new DeviceRegistrationResponse(
            Id: registration.Id,
            InstallationId: registration.InstallationId,
            Platform: registration.Platform,
            RegisteredAtUtc: registration.CreatedAtUtc,
            ExpiresAtUtc: registration.ExpiresAtUtc));
    }

    private static async Task<IResult> SimulateUnregisterDevice(
        string installationId,
        ClaimsPrincipal user,
        RestaurantOrderDbContext dbContext)
    {
        var tenantClaim = user.FindFirst(JwtClaimNames.TenantId)?.Value ?? user.FindFirst("tenant_id")?.Value;
        if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var tenantIdTyped = new TenantId(tenantId);
        var isCustomer = string.Equals(user.FindFirst(JwtClaimNames.PrincipalType)?.Value, "customer", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(user.FindFirst(ClaimTypes.Role)?.Value, "Customer", StringComparison.OrdinalIgnoreCase);

        var query = dbContext.DeviceRegistrations
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantIdTyped && d.InstallationId == installationId && d.IsActive);

        if (isCustomer)
        {
            var tableSessionClaim = user.FindFirst(JwtClaimNames.TableSessionId)?.Value ?? user.FindFirst("table_session_id")?.Value;
            if (Guid.TryParse(tableSessionClaim, out var sessionId))
            {
                var sessionTyped = new DiningSessionId(sessionId);
                query = query.Where(d => d.DiningSessionId == sessionTyped);
            }
        }
        else
        {
            var subClaim = user.FindFirst(JwtClaimNames.Subject)?.Value ?? user.FindFirst("sub")?.Value;
            if (Guid.TryParse(subClaim, out var userId))
            {
                var userIdTyped = new UserId(userId);
                query = query.Where(d => d.UserId == userIdTyped);
            }
        }

        var devices = await query.ToListAsync();
        foreach (var d in devices)
        {
            d.Deactivate(now);
        }

        if (devices.Count > 0)
        {
            await dbContext.SaveChangesAsync();
        }

        return Results.NoContent();
    }
}
