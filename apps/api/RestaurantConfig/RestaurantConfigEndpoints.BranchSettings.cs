using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Api.RestaurantConfig;

public sealed record UpdateBranchSettingsApiRequest(
    string Timezone,
    string Currency,
    string DefaultLocale,
    IReadOnlyList<string> SupportedLocales,
    bool PricesIncludeTax,
    int DefaultTaxRateBps,
    bool IsServiceChargeEnabled,
    int ServiceChargeRateBps,
    bool IsOrderTakingEnabled,
    string? DisplayName = null,
    string? PhoneNumber = null,
    string? Email = null,
    string? Address = null,
    Guid? ConcurrencyToken = null);

public sealed record TimeSlotApiRequest(
    string OpenTime,
    string CloseTime);

public sealed record OperatingDayScheduleApiRequest(
    int DayOfWeek,
    bool IsClosed,
    IReadOnlyList<TimeSlotApiRequest>? Slots = null);

public sealed record UpdateBranchOperatingHoursApiRequest(
    IReadOnlyList<OperatingDayScheduleApiRequest> Days,
    Guid? ConcurrencyToken = null);

public static partial class RestaurantConfigEndpoints
{
    private static void MapBranchSettingsEndpoints(RouteGroupBuilder root)
    {
        // 1. Branch Operational & Financial Settings
        var branchSettingsGroup = root.MapGroup("/branches/{branchId:guid}/settings");

        branchSettingsGroup.MapGet("/", async (
            Guid branchId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var settings = await service.GetEffectiveBranchSettingsAsync(tenantId, new BranchId(branchId), actor, ct);

                if (settings.ConcurrencyToken != Guid.Empty)
                {
                    httpContext.Response.Headers.ETag = $"\"{settings.ConcurrencyToken:D}\"";
                }
                return Results.Ok(settings);
            }
            catch (ResourceNotFoundException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: ex.Message, type: "https://httpstatuses.com/404");
            }
            catch (InvalidAuthorizationScopeException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message, type: "https://httpstatuses.com/403");
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationView)
        .WithName("GetBranchSettings")
        .WithSummary("Get effective operational and financial settings for a branch");

        branchSettingsGroup.MapPut("/", async (
            Guid branchId,
            UpdateBranchSettingsApiRequest request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for branch settings updates.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var command = new UpdateBranchSettingsCommand(
                    Timezone: request.Timezone,
                    Currency: request.Currency,
                    DefaultLocale: request.DefaultLocale,
                    SupportedLocales: request.SupportedLocales,
                    PricesIncludeTax: request.PricesIncludeTax,
                    DefaultTaxRateBps: request.DefaultTaxRateBps,
                    IsServiceChargeEnabled: request.IsServiceChargeEnabled,
                    ServiceChargeRateBps: request.ServiceChargeRateBps,
                    IsOrderTakingEnabled: request.IsOrderTakingEnabled,
                    DisplayName: request.DisplayName,
                    PhoneNumber: request.PhoneNumber,
                    Email: request.Email,
                    Address: request.Address,
                    ConcurrencyToken: token);

                var settings = await service.UpdateBranchSettingsAsync(tenantId, new BranchId(branchId), command, actor, ct);
                httpContext.Response.Headers.ETag = $"\"{settings.ConcurrencyToken:D}\"";
                return Results.Ok(settings);
            }
            catch (ResourceNotFoundException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: ex.Message, type: "https://httpstatuses.com/404");
            }
            catch (ConcurrencyConflictException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: ex.Message, type: "https://httpstatuses.com/409");
            }
            catch (InvalidAuthorizationScopeException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message, type: "https://httpstatuses.com/403");
            }
            catch (DomainException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ex.Message, type: "https://httpstatuses.com/400");
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("UpdateBranchSettings")
        .WithSummary("Update operational and financial settings for a branch");

        // 2. Branch Operating Hours
        var operatingHoursGroup = root.MapGroup("/branches/{branchId:guid}/operating-hours");

        operatingHoursGroup.MapGet("/", async (
            Guid branchId,
            ITenantContext tenantContext,
            IRestaurantConfigService service,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var hours = await service.GetBranchOperatingHoursAsync(tenantId, new BranchId(branchId), actor, ct);

                if (hours.ConcurrencyToken != Guid.Empty)
                {
                    httpContext.Response.Headers.ETag = $"\"{hours.ConcurrencyToken:D}\"";
                }
                return Results.Ok(hours);
            }
            catch (ResourceNotFoundException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: ex.Message, type: "https://httpstatuses.com/404");
            }
            catch (InvalidAuthorizationScopeException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message, type: "https://httpstatuses.com/403");
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationView)
        .WithName("GetBranchOperatingHours")
        .WithSummary("Get weekly operating hours schedule for a branch");

        operatingHoursGroup.MapPut("/", async (
            Guid branchId,
            UpdateBranchOperatingHoursApiRequest request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for operating hours updates.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);

                var days = (request.Days ?? Array.Empty<OperatingDayScheduleApiRequest>())
                    .Select(d => new OperatingDayScheduleDto(
                        d.DayOfWeek,
                        d.IsClosed,
                        (d.Slots ?? Array.Empty<TimeSlotApiRequest>())
                            .Select(s => new TimeSlotDto(s.OpenTime, s.CloseTime, false))
                            .ToList()))
                    .ToList();

                var command = new UpdateBranchOperatingHoursCommand(days, token);

                var hours = await service.UpdateBranchOperatingHoursAsync(tenantId, new BranchId(branchId), command, actor, ct);
                httpContext.Response.Headers.ETag = $"\"{hours.ConcurrencyToken:D}\"";
                return Results.Ok(hours);
            }
            catch (ResourceNotFoundException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: ex.Message, type: "https://httpstatuses.com/404");
            }
            catch (ConcurrencyConflictException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: ex.Message, type: "https://httpstatuses.com/409");
            }
            catch (InvalidAuthorizationScopeException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message, type: "https://httpstatuses.com/403");
            }
            catch (DomainException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ex.Message, type: "https://httpstatuses.com/400");
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.BranchConfigurationManage)
        .WithName("UpdateBranchOperatingHours")
        .WithSummary("Update weekly operating hours schedule for a branch");
    }
}
