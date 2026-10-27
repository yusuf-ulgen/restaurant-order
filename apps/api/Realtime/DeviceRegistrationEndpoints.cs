using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

        group.MapPost("/", (
            RegisterDeviceRequest request,
            ClaimsPrincipal user,
            ICustomerSessionValidator sessionValidator,
            RestaurantOrderDbContext dbContext,
            CancellationToken ct) => RegisterDeviceHandlerAsync(request, user, sessionValidator, dbContext, ct))
        .WithName("RegisterDevice");

        group.MapDelete("/{installationId}", (
            string installationId,
            ClaimsPrincipal user,
            RestaurantOrderDbContext dbContext,
            CancellationToken ct) => UnregisterDeviceHandlerAsync(installationId, user, dbContext, ct))
        .WithName("UnregisterDevice");

        return endpoints;
    }

    internal static async Task<IResult> RegisterDeviceHandlerAsync(
        RegisterDeviceRequest request,
        ClaimsPrincipal user,
        ICustomerSessionValidator sessionValidator,
        RestaurantOrderDbContext dbContext,
        CancellationToken ct = default)
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

            var isSessionActive = await sessionValidator.ValidateSessionActiveAsync(tenantId, branchId, sessionId, ct);
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
            .FirstOrDefaultAsync(d => d.TenantId == tenantIdTyped && d.InstallationId == request.InstallationId, ct);

        var (evalResult, registration) = await EvaluateRegistrationAsync(
            existing,
            request,
            isCustomer,
            tenantIdTyped,
            branchIdTyped,
            sessionTyped,
            userIdTyped,
            sessionValidator,
            dbContext,
            now,
            ct);

        if (evalResult != null)
        {
            return evalResult;
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            dbContext.ChangeTracker.Clear();

            var reloaded = await dbContext.DeviceRegistrations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.TenantId == tenantIdTyped && d.InstallationId == request.InstallationId, ct);

            if (reloaded == null)
            {
                throw;
            }

            var (retryEvalResult, retryReg) = await EvaluateRegistrationAsync(
                reloaded,
                request,
                isCustomer,
                tenantIdTyped,
                branchIdTyped,
                sessionTyped,
                userIdTyped,
                sessionValidator,
                dbContext,
                now,
                ct);

            if (retryEvalResult != null)
            {
                return retryEvalResult;
            }

            registration = retryReg;
            await dbContext.SaveChangesAsync(ct);
        }

        return Results.Ok(new DeviceRegistrationResponse(
            Id: registration!.Id,
            InstallationId: registration.InstallationId,
            Platform: registration.Platform,
            RegisteredAtUtc: registration.CreatedAtUtc,
            ExpiresAtUtc: registration.ExpiresAtUtc));
    }

    internal static async Task<IResult> UnregisterDeviceHandlerAsync(
        string installationId,
        ClaimsPrincipal user,
        RestaurantOrderDbContext dbContext,
        CancellationToken ct = default)
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
    }

    private static async Task<(IResult? ErrorResult, DeviceRegistration? Registration)> EvaluateRegistrationAsync(
        DeviceRegistration? existing,
        RegisterDeviceRequest request,
        bool isCustomer,
        TenantId tenantIdTyped,
        BranchId branchIdTyped,
        DiningSessionId? sessionTyped,
        UserId? userIdTyped,
        ICustomerSessionValidator sessionValidator,
        RestaurantOrderDbContext dbContext,
        DateTimeOffset now,
        CancellationToken ct)
    {
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
            return (null, newReg);
        }

        // Ownership and rebind validation
        if (isCustomer)
        {
            // Case 1: Same owner renewal
            if (existing.DiningSessionId == sessionTyped && existing.BranchId == branchIdTyped && existing.UserId == null)
            {
                existing.Renew(now.AddDays(1), now);
                return (null, existing);
            }

            // Case 2: Cross-branch rejection when active
            if (existing.BranchId != branchIdTyped && existing.IsActive)
            {
                return (Results.Conflict(new ProblemDetails
                {
                    Title = "Device registered to another branch",
                    Detail = "This device installation is currently active in another branch.",
                    Status = StatusCodes.Status409Conflict
                }), null);
            }

            // Case 3: Device currently owned by active customer session
            if (existing.DiningSessionId != null && existing.IsActive)
            {
                var isPrevSessionActive = await sessionValidator.ValidateSessionActiveAsync(
                    existing.TenantId.Value,
                    existing.BranchId.Value,
                    existing.DiningSessionId.Value.Value,
                    ct);

                if (isPrevSessionActive)
                {
                    // Previous session is still active: prevent hijacking / invalid renewal
                    return (Results.Conflict(new ProblemDetails
                    {
                        Title = "Device registered to another active session",
                        Detail = "This device installation is currently registered to another active dining session.",
                        Status = StatusCodes.Status409Conflict
                    }), null);
                }

                // Previous session closed: safe rebind from Masa A to Masa B
                existing.RebindToCustomer(branchIdTyped, sessionTyped!.Value, request.Platform, now, TimeSpan.FromDays(1));
                return (null, existing);
            }

            // Case 4: Device registered to staff or inactive
            if (existing.UserId != null && existing.IsActive)
            {
                return (Results.Conflict(new ProblemDetails
                {
                    Title = "Device registered to staff",
                    Detail = "This device installation is currently registered to a staff member.",
                    Status = StatusCodes.Status409Conflict
                }), null);
            }

            // Safe rebind for previously inactive device
            existing.RebindToCustomer(branchIdTyped, sessionTyped!.Value, request.Platform, now, TimeSpan.FromDays(1));
            return (null, existing);
        }
        else
        {
            // Staff caller
            // Case 1: Same staff user renewal
            if (existing.UserId == userIdTyped && existing.BranchId == branchIdTyped && existing.DiningSessionId == null)
            {
                existing.Renew(now.AddDays(30), now);
                return (null, existing);
            }

            // Case 2: Cross-branch rejection when active
            if (existing.BranchId != branchIdTyped && existing.IsActive)
            {
                return (Results.Conflict(new ProblemDetails
                {
                    Title = "Device registered to another branch",
                    Detail = "This device installation is currently active in another branch.",
                    Status = StatusCodes.Status409Conflict
                }), null);
            }

            // Case 3: Device active for another staff or customer session
            if (existing.IsActive)
            {
                return (Results.Conflict(new ProblemDetails
                {
                    Title = "Device registered to another user or session",
                    Detail = "This device installation is currently active for another user or session.",
                    Status = StatusCodes.Status409Conflict
                }), null);
            }

            // Safe rebind for inactive device to new staff user
            existing.RebindToStaff(branchIdTyped, userIdTyped!.Value, request.Platform, now, TimeSpan.FromDays(30));
            return (null, existing);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            return true;
        }

        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("23505", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
    }
}
