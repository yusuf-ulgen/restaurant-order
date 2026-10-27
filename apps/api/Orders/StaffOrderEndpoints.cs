using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.Tenancy;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.Orders.Pricing;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Api.Orders;

public static class StaffOrderEndpoints
{
    public static IEndpointRouteBuilder MapStaffOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/floor/branches/{branchId:guid}/sessions/{sessionId:guid}/orders")
            .WithTags("Staff Orders")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "RestaurantAdmin,BranchManager,Cashier,Waiter" });

        // POST /api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders
        group.MapPost("", (
            Guid branchId,
            Guid sessionId,
            [FromBody] CreateStaffOrderRequest request,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ITenantContext tenantContext,
            IStaffOrderService staffOrderService,
            CancellationToken ct) => CreateStaffOrderAsync(branchId, sessionId, request, context, parser, tenantContext, staffOrderService, ct))
        .WithName("CreateStaffOrder")
        .WithSummary("Create a staff round of ordering for an active dining session");

        // GET /api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders
        group.MapGet("", (
            Guid branchId,
            Guid sessionId,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ITenantContext tenantContext,
            IStaffOrderService staffOrderService,
            CancellationToken ct) => GetSessionStaffOrdersAsync(branchId, sessionId, context, parser, tenantContext, staffOrderService, ct))
        .WithName("GetSessionStaffOrders")
        .WithSummary("List all orders for an active dining session");

        // GET /api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}
        group.MapGet("/{orderId:guid}", (
            Guid branchId,
            Guid sessionId,
            Guid orderId,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ITenantContext tenantContext,
            IStaffOrderService staffOrderService,
            CancellationToken ct) => GetStaffOrderByIdAsync(branchId, sessionId, orderId, context, parser, tenantContext, staffOrderService, ct))
        .WithName("GetStaffOrderById")
        .WithSummary("Get order details for an active dining session");

        // POST /api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel
        group.MapPost("/{orderId:guid}/items/{itemId:guid}/cancel", (
            Guid branchId,
            Guid sessionId,
            Guid orderId,
            Guid itemId,
            [FromBody] CancelOrderItemRequest request,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ITenantContext tenantContext,
            IStaffOrderService staffOrderService,
            CancellationToken ct) => CancelStaffOrderItemAsync(branchId, sessionId, orderId, itemId, request, context, parser, tenantContext, staffOrderService, ct))
        .WithName("CancelStaffOrderItem")
        .WithSummary("Cancel a pre-prep item or supervisor void an in-prep/ready/served item");

        var branchOrdersGroup = app.MapGroup("/api/v1/floor/branches/{branchId:guid}/orders")
            .WithTags("Staff Orders")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "RestaurantAdmin,BranchManager,Cashier,Waiter" });

        // GET /api/v1/floor/branches/{branchId}/orders/ready-items
        branchOrdersGroup.MapGet("/ready-items", (
            Guid branchId,
            [FromQuery] Guid? sessionId,
            [FromQuery] Guid? tableId,
            [FromQuery] int? limit,
            [FromQuery] int? offset,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ITenantContext tenantContext,
            IStaffOrderService staffOrderService,
            CancellationToken ct) => GetBranchReadyItemsAsync(branchId, sessionId, tableId, limit, offset, context, parser, tenantContext, staffOrderService, ct))
        .WithName("GetBranchReadyItems")
        .WithSummary("List all prepared items that are ready for table delivery");

        return app;
    }

    internal static async Task<IResult> CreateStaffOrderAsync(
        Guid branchId,
        Guid sessionId,
        CreateStaffOrderRequest request,
        HttpContext context,
        IJwtClaimPrincipalParser parser,
        ITenantContext tenantContext,
        IStaffOrderService staffOrderService,
        CancellationToken ct)
    {
        try
        {
            var actor = FloorEndpointHelpers.GetActor(context, parser);
            var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);

            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var rawKey) ||
                string.IsNullOrWhiteSpace(rawKey))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "Idempotency-Key header is required.");
            }

            var idempotencyKey = rawKey.ToString().Trim();
            var result = await staffOrderService.CreateStaffOrderAsync(
                tenantId,
                new BranchId(branchId),
                new DiningSessionId(sessionId),
                request,
                idempotencyKey,
                actor,
                ct);

            context.Response.Headers.ETag = $"\"{result.Order.ConcurrencyToken:D}\"";

            if (result.IsReplay)
            {
                return Results.Ok(result.Order);
            }

            return Results.Created(
                $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{result.Order.Id}",
                result.Order);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }

    internal static async Task<IResult> GetSessionStaffOrdersAsync(
        Guid branchId,
        Guid sessionId,
        HttpContext context,
        IJwtClaimPrincipalParser parser,
        ITenantContext tenantContext,
        IStaffOrderService staffOrderService,
        CancellationToken ct)
    {
        try
        {
            var actor = FloorEndpointHelpers.GetActor(context, parser);
            var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);

            var orders = await staffOrderService.GetSessionOrdersAsync(
                tenantId,
                new BranchId(branchId),
                new DiningSessionId(sessionId),
                actor,
                ct);

            return Results.Ok(orders);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }

    internal static async Task<IResult> GetStaffOrderByIdAsync(
        Guid branchId,
        Guid sessionId,
        Guid orderId,
        HttpContext context,
        IJwtClaimPrincipalParser parser,
        ITenantContext tenantContext,
        IStaffOrderService staffOrderService,
        CancellationToken ct)
    {
        try
        {
            var actor = FloorEndpointHelpers.GetActor(context, parser);
            var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);

            var order = await staffOrderService.GetOrderByIdAsync(
                tenantId,
                new BranchId(branchId),
                new DiningSessionId(sessionId),
                new OrderId(orderId),
                actor,
                ct);

            context.Response.Headers.ETag = $"\"{order.ConcurrencyToken:D}\"";
            return Results.Ok(order);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }

    internal static async Task<IResult> CancelStaffOrderItemAsync(
        Guid branchId,
        Guid sessionId,
        Guid orderId,
        Guid itemId,
        CancelOrderItemRequest request,
        HttpContext context,
        IJwtClaimPrincipalParser parser,
        ITenantContext tenantContext,
        IStaffOrderService staffOrderService,
        CancellationToken ct)
    {
        try
        {
            var actor = FloorEndpointHelpers.GetActor(context, parser);
            var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);
            var concurrencyToken = FloorEndpointHelpers.ExtractConcurrencyToken(request?.ConcurrencyToken, context.Request);

            var updatedOrder = await staffOrderService.CancelOrderItemAsync(
                tenantId,
                new BranchId(branchId),
                new DiningSessionId(sessionId),
                new OrderId(orderId),
                new OrderItemId(itemId),
                request!,
                concurrencyToken,
                actor,
                ct);

            context.Response.Headers.ETag = $"\"{updatedOrder.ConcurrencyToken:D}\"";
            return Results.Ok(updatedOrder);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }

    internal static async Task<IResult> GetBranchReadyItemsAsync(
        Guid branchId,
        Guid? sessionId,
        Guid? tableId,
        int? limit,
        int? offset,
        HttpContext context,
        IJwtClaimPrincipalParser parser,
        ITenantContext tenantContext,
        IStaffOrderService staffOrderService,
        CancellationToken ct)
    {
        try
        {
            var actor = FloorEndpointHelpers.GetActor(context, parser);
            var tenantId = FloorEndpointHelpers.ResolveTenantId(tenantContext, actor);

            var items = await staffOrderService.ListReadyItemsAsync(
                tenantId,
                new BranchId(branchId),
                sessionId.HasValue ? new DiningSessionId(sessionId.Value) : null,
                tableId.HasValue ? new RestaurantTableId(tableId.Value) : null,
                limit ?? 50,
                offset ?? 0,
                actor,
                ct);

            return Results.Ok(items);
        }
        catch (Exception ex)
        {
            return HandleException(ex, context);
        }
    }

    internal static IResult HandleException(Exception ex, HttpContext context)
    {
        var correlationId = context.TraceIdentifier;
        var extensions = new Dictionary<string, object?>
        {
            ["correlationId"] = correlationId
        };

        return ex switch
        {
            OrderPricingException ope =>
                Results.Problem(
                    statusCode: ope.ErrorCode switch
                    {
                        OrderPricingException.CodePriceChanged => StatusCodes.Status409Conflict,
                        OrderPricingException.CodePricingPolicyChanged => StatusCodes.Status409Conflict,
                        OrderPricingException.CodeOrderTotalChanged => StatusCodes.Status409Conflict,
                        OrderPricingException.CodeItemOutOfStock => StatusCodes.Status409Conflict,
                        OrderPricingException.CodeSessionNotActive => StatusCodes.Status409Conflict,
                        OrderPricingException.CodeOrderTakingDisabled => StatusCodes.Status403Forbidden,
                        _ => StatusCodes.Status400BadRequest
                    },
                    title: ope.ErrorCode switch
                    {
                        OrderPricingException.CodePriceChanged => "Order Price Changed",
                        OrderPricingException.CodePricingPolicyChanged => "Pricing Policy Changed",
                        OrderPricingException.CodeOrderTotalChanged => "Order Total Changed",
                        OrderPricingException.CodeItemOutOfStock => "Item Out of Stock",
                        OrderPricingException.CodeSessionNotActive => "Session Not Active",
                        OrderPricingException.CodeOrderTakingDisabled => "Order Taking Disabled",
                        _ => "Invalid Modifier Selection"
                    },
                    detail: ope.Message,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = ope.ErrorCode,
                        ["correlationId"] = correlationId
                    }),

            IdempotencyConflictException ice =>
                Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: ice.Message,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = IdempotencyConflictException.ErrorCode,
                        ["correlationId"] = correlationId
                    }),

            ResourceNotFoundException rnfe =>
                Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: rnfe.Message,
                    extensions: extensions),

            ConcurrencyPreconditionException cpe =>
                Results.Problem(
                    statusCode: StatusCodes.Status412PreconditionFailed,
                    title: "Precondition Failed",
                    detail: cpe.Message,
                    extensions: extensions),

            ConcurrencyConflictException cce =>
                Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: cce.Message,
                    extensions: extensions),

            InvalidAuthorizationScopeException iase =>
                Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: iase.Message,
                    extensions: extensions),

            DomainException de =>
                Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: de.Message,
                    extensions: extensions),

            DbUpdateConcurrencyException =>
                Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: "Eşzamanlı işlem çakışması tespit edildi. Lütfen tekrar deneyiniz.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "CONCURRENCY_CONFLICT",
                        ["correlationId"] = correlationId
                    }),

            DbUpdateException dbEx when IsKnownOrderConstraintViolation(dbEx) =>
                Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: "Eşzamanlı işlem çakışması tespit edildi. Lütfen tekrar deneyiniz.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "CONCURRENCY_CONFLICT",
                        ["correlationId"] = correlationId
                    }),

            _ => FloorEndpointHelpers.HandleException(ex, context)
        };
    }

    internal static bool IsKnownOrderConstraintViolation(DbUpdateException dbEx)
    {
        if (dbEx.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
        {
            return pgEx.ConstraintName is "ix_orders_tenant_session_round" or "ix_orders_tenant_idempotency_key";
        }

        return false;
    }
}
