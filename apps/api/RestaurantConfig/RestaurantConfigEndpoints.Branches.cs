using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Api.RestaurantConfig;

public static partial class RestaurantConfigEndpoints
{
    private static void MapBranchEndpoints(RouteGroupBuilder root)
    {
        var group = root.MapGroup("/branches");

        group.MapGet("/", async (
            Guid? brandId,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var actor = GetActor(httpContext, parser);
            if (actor.Role != AuthRole.RestaurantAdmin && actor.Role != AuthRole.BranchManager)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "You do not have permission to view branches.", type: "https://httpstatuses.com/403");
            }

            var tenantId = ResolveTenantId(tenantContext, actor);
            BrandId? filterBrandId = brandId.HasValue ? new BrandId(brandId.Value) : null;
            var branches = await service.ListBranchesAsync(tenantId, actor, filterBrandId, ct);
            return Results.Ok(branches);
        })
        .RequireAuthorization()
        .WithName("ListBranches")
        .WithSummary("List branches for tenant, optionally filtered by brand ID");

        group.MapGet("/{branchId:guid}", async (
            Guid branchId,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var actor = GetActor(httpContext, parser);
            if (actor.Role != AuthRole.RestaurantAdmin && actor.Role != AuthRole.BranchManager)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "You do not have permission to view this branch.", type: "https://httpstatuses.com/403");
            }

            try
            {
                var tenantId = ResolveTenantId(tenantContext, actor);
                var branch = await service.GetBranchByIdAsync(tenantId, new BranchId(branchId), actor, ct);
                if (branch == null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: $"Branch '{branchId}' not found.", type: "https://httpstatuses.com/404");
                }
                return BranchResult(httpContext, branch);
            }
            catch (InvalidAuthorizationScopeException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message, type: "https://httpstatuses.com/403");
            }
        })
        .RequireAuthorization()
        .WithName("GetBranch")
        .WithSummary("Get branch details by ID");

        group.MapPost("/", async (
            CreateBranchApiRequest request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var command = new CreateBranchCommand(request.BrandId, request.Name, request.Slug, request.Timezone, request.Currency);
                var branch = await service.CreateBranchAsync(tenantId, command, actor, ct);
                return BranchResult(httpContext, branch, StatusCodes.Status201Created);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBranchesManage)
        .WithName("CreateBranch")
        .WithSummary("Create a new branch in tenant");

        group.MapPut("/{branchId:guid}", async (
            Guid branchId,
            UpdateBranchApiRequest request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for branch updates.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var command = new UpdateBranchCommand(request.Name, request.Timezone, request.Currency, token);
                var branch = await service.UpdateBranchAsync(tenantId, new BranchId(branchId), command, actor, ct);
                return BranchResult(httpContext, branch);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBranchesManage)
        .WithName("UpdateBranch")
        .WithSummary("Update branch details with optimistic concurrency control");

        group.MapPost("/{branchId:guid}/activate", async (
            Guid branchId,
            BranchStateApiRequest? request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for branch activation.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var command = new BranchStateChangeCommand(request?.Reason, token);
                var branch = await service.ActivateBranchAsync(tenantId, new BranchId(branchId), command, actor, ct);
                return BranchResult(httpContext, branch);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBranchesManage)
        .WithName("ActivateBranch")
        .WithSummary("Activate a suspended branch");

        group.MapPost("/{branchId:guid}/suspend", async (
            Guid branchId,
            BranchStateApiRequest? request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for branch suspension.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var command = new BranchStateChangeCommand(request?.Reason, token);
                var branch = await service.SuspendBranchAsync(tenantId, new BranchId(branchId), command, actor, ct);
                return BranchResult(httpContext, branch);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBranchesManage)
        .WithName("SuspendBranch")
        .WithSummary("Suspend an active branch");

        group.MapPost("/{branchId:guid}/close", async (
            Guid branchId,
            BranchStateApiRequest? request,
            ITenantContext tenantContext,
            HttpContext httpContext,
            IJwtClaimPrincipalParser parser,
            IRestaurantConfigService service,
            CancellationToken ct) =>
        {
            var token = ExtractConcurrencyToken(request?.ConcurrencyToken, httpContext.Request);
            if (token == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition Failed", detail: "Concurrency token or If-Match header is required for closing a branch.", type: "https://httpstatuses.com/412");
            }

            try
            {
                var actor = GetActor(httpContext, parser);
                var tenantId = ResolveTenantId(tenantContext, actor);
                var command = new BranchStateChangeCommand(request?.Reason, token);
                var branch = await service.CloseBranchAsync(tenantId, new BranchId(branchId), command, actor, ct);
                return BranchResult(httpContext, branch);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        })
        .RequireAuthorization()
        .RequirePermission(Permissions.TenantBranchesManage)
        .WithName("CloseBranch")
        .WithSummary("Permanently close a branch");
    }
}
