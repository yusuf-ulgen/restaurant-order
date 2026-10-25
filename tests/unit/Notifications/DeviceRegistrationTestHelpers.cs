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

    public static async Task<IResult> SimulateRegisterDevice(
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

        DiningSessionId? sessionTyped = null;
        UserId? userIdTyped = null;

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

            sessionTyped = new DiningSessionId(sessionId);
        }
        else
        {
            var subClaim = user.FindFirst(JwtClaimNames.Subject)?.Value ?? user.FindFirst("sub")?.Value;
            if (!Guid.TryParse(subClaim, out var staffUserId) || staffUserId == Guid.Empty)
            {
                return Results.Unauthorized();
            }

            userIdTyped = new UserId(staffUserId);
        }

        var existing = await dbContext.DeviceRegistrations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.TenantId == tenantIdTyped && d.InstallationId == request.InstallationId);

        if (existing == null)
        {
            DeviceRegistration newReg;
            if (isCustomer)
            {
                newReg = DeviceRegistration.CreateForCustomer(
                    tenantIdTyped,
                    branchIdTyped,
                    sessionTyped!.Value,
                    request.InstallationId,
                    request.Platform,
                    now,
                    TimeSpan.FromDays(1));
            }
            else
            {
                newReg = DeviceRegistration.CreateForStaff(
                    tenantIdTyped,
                    branchIdTyped,
                    userIdTyped!.Value,
                    request.InstallationId,
                    request.Platform,
                    now,
                    TimeSpan.FromDays(30));
            }

            dbContext.DeviceRegistrations.Add(newReg);
            await dbContext.SaveChangesAsync();

            return Results.Ok(new DeviceRegistrationResponse(
                Id: newReg.Id,
                InstallationId: newReg.InstallationId,
                Platform: newReg.Platform,
                RegisteredAtUtc: newReg.CreatedAtUtc,
                ExpiresAtUtc: newReg.ExpiresAtUtc));
        }

        if (isCustomer)
        {
            if (existing.DiningSessionId == sessionTyped && existing.BranchId == branchIdTyped && existing.UserId == null)
            {
                existing.Renew(now.AddDays(1), now);
                await dbContext.SaveChangesAsync();
                return Results.Ok(new DeviceRegistrationResponse(
                    Id: existing.Id,
                    InstallationId: existing.InstallationId,
                    Platform: existing.Platform,
                    RegisteredAtUtc: existing.CreatedAtUtc,
                    ExpiresAtUtc: existing.ExpiresAtUtc));
            }

            if (existing.BranchId != branchIdTyped && existing.IsActive)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "Device registered to another branch",
                    Detail = "This device installation is currently active in another branch.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            if (existing.DiningSessionId != null && existing.IsActive)
            {
                var isPrevSessionActive = await sessionValidator.ValidateSessionActiveAsync(
                    existing.TenantId.Value,
                    existing.BranchId.Value,
                    existing.DiningSessionId.Value.Value);

                if (isPrevSessionActive)
                {
                    return Results.Conflict(new ProblemDetails
                    {
                        Title = "Device registered to another active session",
                        Detail = "This device installation is currently registered to another active dining session.",
                        Status = StatusCodes.Status409Conflict
                    });
                }

                existing.RebindToCustomer(branchIdTyped, sessionTyped!.Value, request.Platform, now, TimeSpan.FromDays(1));
                await dbContext.SaveChangesAsync();
                return Results.Ok(new DeviceRegistrationResponse(
                    Id: existing.Id,
                    InstallationId: existing.InstallationId,
                    Platform: existing.Platform,
                    RegisteredAtUtc: existing.CreatedAtUtc,
                    ExpiresAtUtc: existing.ExpiresAtUtc));
            }

            if (existing.UserId != null && existing.IsActive)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "Device registered to staff",
                    Detail = "This device installation is currently registered to a staff member.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            existing.RebindToCustomer(branchIdTyped, sessionTyped!.Value, request.Platform, now, TimeSpan.FromDays(1));
            await dbContext.SaveChangesAsync();
            return Results.Ok(new DeviceRegistrationResponse(
                Id: existing.Id,
                InstallationId: existing.InstallationId,
                Platform: existing.Platform,
                RegisteredAtUtc: existing.CreatedAtUtc,
                ExpiresAtUtc: existing.ExpiresAtUtc));
        }
        else
        {
            if (existing.UserId == userIdTyped && existing.BranchId == branchIdTyped && existing.DiningSessionId == null)
            {
                existing.Renew(now.AddDays(30), now);
                await dbContext.SaveChangesAsync();
                return Results.Ok(new DeviceRegistrationResponse(
                    Id: existing.Id,
                    InstallationId: existing.InstallationId,
                    Platform: existing.Platform,
                    RegisteredAtUtc: existing.CreatedAtUtc,
                    ExpiresAtUtc: existing.ExpiresAtUtc));
            }

            if (existing.BranchId != branchIdTyped && existing.IsActive)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "Device registered to another branch",
                    Detail = "This device installation is currently active in another branch.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            if (existing.IsActive)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "Device registered to another user or session",
                    Detail = "This device installation is currently active for another user or session.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            existing.RebindToStaff(branchIdTyped, userIdTyped!.Value, request.Platform, now, TimeSpan.FromDays(30));
            await dbContext.SaveChangesAsync();
            return Results.Ok(new DeviceRegistrationResponse(
                Id: existing.Id,
                InstallationId: existing.InstallationId,
                Platform: existing.Platform,
                RegisteredAtUtc: existing.CreatedAtUtc,
                ExpiresAtUtc: existing.ExpiresAtUtc));
        }
    }

    public static async Task<IResult> SimulateUnregisterDevice(
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
            if (!Guid.TryParse(tableSessionClaim, out var sessionId) || sessionId == Guid.Empty)
            {
                return Results.Unauthorized();
            }

            var sessionTyped = new DiningSessionId(sessionId);
            query = query.Where(d => d.DiningSessionId == sessionTyped);
        }
        else
        {
            var subClaim = user.FindFirst(JwtClaimNames.Subject)?.Value ?? user.FindFirst("sub")?.Value;
            if (!Guid.TryParse(subClaim, out var userId) || userId == Guid.Empty)
            {
                return Results.Unauthorized();
            }

            var userIdTyped = new UserId(userId);
            query = query.Where(d => d.UserId == userIdTyped);
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
