using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Redis;

namespace RestaurantOrder.Api.Orders;

public static class CustomerOrderEndpoints
{
    public static IEndpointRouteBuilder MapCustomerOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customer/orders")
            .WithTags("Customer Orders")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "Customer" });

        // POST /api/v1/customer/orders
        group.MapPost("", (
            [FromBody] CreateCustomerOrderRequest request,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            IPermissionRegistry permissionRegistry,
            ICustomerOrderService orderService,
            CancellationToken ct) => CreateCustomerOrderAsync(request, context, parser, permissionRegistry, orderService, ct))
        .WithName("CreateCustomerOrder")
        .WithSummary("Place a new customer round of ordering with authoritative pricing and idempotency.");

        // GET /api/v1/customer/orders
        group.MapGet("", (
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ICustomerOrderService orderService,
            CancellationToken ct) => GetCustomerOrdersAsync(context, parser, orderService, ct))
        .WithName("GetCustomerOrders")
        .WithSummary("List all orders placed in the customer's active dining session.");

        // GET /api/v1/customer/orders/{orderId:guid}
        group.MapGet("/{orderId:guid}", (
            [FromRoute] Guid orderId,
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            ICustomerOrderService orderService,
            CancellationToken ct) => GetCustomerOrderByIdAsync(orderId, context, parser, orderService, ct))
        .WithName("GetCustomerOrderById")
        .WithSummary("Retrieve a specific order from the customer's active dining session.");

        return app;
    }

    internal static async Task<IResult> CreateCustomerOrderAsync(
        CreateCustomerOrderRequest request,
        HttpContext context,
        IJwtClaimPrincipalParser parser,
        IPermissionRegistry permissionRegistry,
        ICustomerOrderService orderService,
        CancellationToken ct)
    {
        try
        {
            var actor = FloorEndpointHelpers.GetActor(context, parser);
            if (actor.Role != AuthRole.Customer)
            {
                return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", "Only customers may place QR orders.");
            }

            if (!permissionRegistry.IsPermitted(actor, Permissions.OrdersQrCreate))
            {
                return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", "Missing permission 'orders.qr.create'.");
            }

            if (!actor.Scope.TenantId.HasValue ||
                !actor.Scope.BranchId.HasValue ||
                !actor.Scope.TableSessionId.HasValue)
            {
                return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Customer token is missing required session claims.");
            }

            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var rawKey) ||
                string.IsNullOrWhiteSpace(rawKey))
            {
                return ProblemResult(context, StatusCodes.Status400BadRequest, "Bad Request", "Idempotency-Key header is required.");
            }

            var idempotencyKey = rawKey.ToString().Trim();
            var tenantId = new TenantId(actor.Scope.TenantId.Value);
            var branchId = new BranchId(actor.Scope.BranchId.Value);
            var sessionId = new DiningSessionId(actor.Scope.TableSessionId.Value);

            var result = await orderService.CreateOrderAsync(
                tenantId,
                branchId,
                sessionId,
                request,
                idempotencyKey,
                actor,
                ct);

            context.Response.Headers.ETag = $"\"{result.Order.ConcurrencyToken:D}\"";

            if (result.IsReplay)
            {
                return Results.Ok(result.Order);
            }

            return Results.Created($"/api/v1/customer/orders/{result.Order.Id}", result.Order);
        }
        catch (Exception ex)
        {
            return HandleOrderException(ex, context);
        }
    }

    internal static async Task<IResult> GetCustomerOrdersAsync(
        HttpContext context,
        IJwtClaimPrincipalParser parser,
        ICustomerOrderService orderService,
        CancellationToken ct)
    {
        try
        {
            var actor = FloorEndpointHelpers.GetActor(context, parser);
            if (actor.Role != AuthRole.Customer)
            {
                return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", "Only customers may view session orders.");
            }

            if (!actor.Scope.TenantId.HasValue ||
                !actor.Scope.BranchId.HasValue ||
                !actor.Scope.TableSessionId.HasValue)
            {
                return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Customer token is missing required session claims.");
            }

            var tenantId = new TenantId(actor.Scope.TenantId.Value);
            var branchId = new BranchId(actor.Scope.BranchId.Value);
            var sessionId = new DiningSessionId(actor.Scope.TableSessionId.Value);

            var orders = await orderService.GetOrdersAsync(tenantId, branchId, sessionId, ct);
            return Results.Ok(orders);
        }
        catch (Exception ex)
        {
            return HandleOrderException(ex, context);
        }
    }

    internal static async Task<IResult> GetCustomerOrderByIdAsync(
        Guid orderId,
        HttpContext context,
        IJwtClaimPrincipalParser parser,
        ICustomerOrderService orderService,
        CancellationToken ct)
    {
        try
        {
            var actor = FloorEndpointHelpers.GetActor(context, parser);
            if (actor.Role != AuthRole.Customer)
            {
                return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", "Only customers may view order details.");
            }

            if (!actor.Scope.TenantId.HasValue ||
                !actor.Scope.BranchId.HasValue ||
                !actor.Scope.TableSessionId.HasValue)
            {
                return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Customer token is missing required session claims.");
            }

            var tenantId = new TenantId(actor.Scope.TenantId.Value);
            var branchId = new BranchId(actor.Scope.BranchId.Value);
            var sessionId = new DiningSessionId(actor.Scope.TableSessionId.Value);

            var order = await orderService.GetOrderByIdAsync(tenantId, branchId, sessionId, new OrderId(orderId), ct);
            if (order == null)
            {
                return ProblemResult(context, StatusCodes.Status404NotFound, "Not Found", "Order was not found in your active session.");
            }

            context.Response.Headers.ETag = $"\"{order.ConcurrencyToken:D}\"";
            return Results.Ok(order);
        }
        catch (Exception ex)
        {
            return HandleOrderException(ex, context);
        }
    }

    internal static IResult HandleOrderException(Exception ex, HttpContext context)
    {
        return ex switch
        {
            IdempotencyConflictException icex =>
                ProblemResult(context, StatusCodes.Status409Conflict, "Conflict", icex.Message, IdempotencyConflictException.ErrorCode),

            OrderPricingException opex => MapPricingException(opex, context),

            DistributedSecurityStateUnavailableException =>
                ProblemResult(context, StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "Güvenlik altyapısı şu anda kullanılamıyor, lütfen kısa süre sonra tekrar deneyiniz.", "DISTRIBUTED_SECURITY_STATE_UNAVAILABLE"),

            DbUpdateConcurrencyException =>
                ProblemResult(context, StatusCodes.Status409Conflict, "Conflict", "Eşzamanlı işlem çakışması tespit edildi. Lütfen tekrar deneyiniz.", "CONCURRENCY_CONFLICT"),

            DbUpdateException dbEx when IsKnownOrderConstraintViolation(dbEx) =>
                ProblemResult(context, StatusCodes.Status409Conflict, "Conflict", "Eşzamanlı işlem çakışması tespit edildi. Lütfen tekrar deneyiniz.", "CONCURRENCY_CONFLICT"),

            DomainException dex =>
                ProblemResult(context, StatusCodes.Status400BadRequest, "Bad Request", dex.Message),

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

    internal static IResult MapPricingException(OrderPricingException ex, HttpContext context)
    {
        var (status, title) = ex.ErrorCode switch
        {
            OrderPricingException.CodePriceChanged => (StatusCodes.Status409Conflict, "Order Price Changed"),
            OrderPricingException.CodePricingPolicyChanged => (StatusCodes.Status409Conflict, "Pricing Policy Changed"),
            OrderPricingException.CodeOrderTotalChanged => (StatusCodes.Status409Conflict, "Order Total Changed"),
            OrderPricingException.CodeItemOutOfStock => (StatusCodes.Status409Conflict, "Item Out of Stock"),
            OrderPricingException.CodeOrderTakingDisabled => (StatusCodes.Status403Forbidden, "Order Taking Disabled"),
            OrderPricingException.CodeSessionNotActive => (StatusCodes.Status409Conflict, "Session Not Active"),
            OrderPricingException.CodeInvalidModifierSelection => (StatusCodes.Status400BadRequest, "Invalid Modifier Selection"),
            OrderPricingException.CodeDuplicateModifier => (StatusCodes.Status400BadRequest, "Duplicate Modifier Selection"),
            OrderPricingException.CodeInvalidVariant => (StatusCodes.Status400BadRequest, "Invalid Variant"),
            OrderPricingException.CodeItemNotActive => (StatusCodes.Status400BadRequest, "Item Not Active"),
            OrderPricingException.CodeCurrencyMismatch => (StatusCodes.Status400BadRequest, "Currency Mismatch"),
            _ => (StatusCodes.Status400BadRequest, "Pricing Error")
        };

        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = ex.ErrorCode
        };

        if (ex.ExpectedUnitPriceMinorUnits.HasValue)
        {
            extensions["previousPriceMinorUnits"] = ex.ExpectedUnitPriceMinorUnits.Value;
        }

        if (ex.ActualUnitPriceMinorUnits.HasValue)
        {
            extensions["currentPriceMinorUnits"] = ex.ActualUnitPriceMinorUnits.Value;
        }

        if (ex.MenuItemId.HasValue)
        {
            extensions["menuItemId"] = ex.MenuItemId.Value.Value;
        }

        if (ex.ItemVariantId.HasValue)
        {
            extensions["itemVariantId"] = ex.ItemVariantId.Value.Value;
        }

        return ProblemResult(context, status, title, ex.Message, ex.ErrorCode, extensions);
    }

    internal static IResult ProblemResult(
        HttpContext context,
        int status,
        string title,
        string detail,
        string? errorCode = null,
        Dictionary<string, object?>? extraExtensions = null)
    {
        var correlationId = context.TraceIdentifier;
        var extensions = extraExtensions ?? new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(errorCode) && !extensions.ContainsKey("errorCode"))
        {
            extensions["errorCode"] = errorCode;
        }

        extensions["correlationId"] = correlationId;

        return Results.Json(new
        {
            type = $"https://httpstatuses.com/{status}",
            title,
            status,
            detail,
            instance = context.Request.Path.Value,
            extensions
        }, statusCode: status, contentType: "application/problem+json");
    }
}
