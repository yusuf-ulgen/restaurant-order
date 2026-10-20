using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

namespace RestaurantOrder.Api.Realtime;

public static class DeviceRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapDeviceRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/devices")
            .RequireAuthorization()
            .WithTags("PushNotifications");

        group.MapPost("/", async (
            RegisterDeviceRequest request,
            ClaimsPrincipal user,
            ICustomerSessionValidator sessionValidator,
            RestaurantOrderDbContext dbContext,
            CancellationToken ct) =>
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

                var isSessionActive = await sessionValidator.ValidateSessionActiveAsync(tenantId, branchId, sessionId, ct);
                if (!isSessionActive)
                {
                    return Results.Forbid();
                }

                var sessionTyped = new DiningSessionId(sessionId);
                var existing = await dbContext.DeviceRegistrations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(d => d.TenantId == tenantIdTyped && d.InstallationId == request.InstallationId, ct);

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
                    .FirstOrDefaultAsync(d => d.TenantId == tenantIdTyped && d.InstallationId == request.InstallationId, ct);

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

            await dbContext.SaveChangesAsync(ct);

            return Results.Ok(new DeviceRegistrationResponse(
                Id: registration.Id,
                InstallationId: registration.InstallationId,
                Platform: registration.Platform,
                RegisteredAtUtc: registration.CreatedAtUtc,
                ExpiresAtUtc: registration.ExpiresAtUtc));
        })
        .WithName("RegisterDevice");

        group.MapDelete("/{installationId}", async (
            string installationId,
            ClaimsPrincipal user,
            RestaurantOrderDbContext dbContext,
            CancellationToken ct) =>
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

            var devices = await query.ToListAsync(ct);
            foreach (var d in devices)
            {
                d.Deactivate(now);
            }

            if (devices.Count > 0)
            {
                await dbContext.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        })
        .WithName("UnregisterDevice");

        return endpoints;
    }
}
