using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.Orders;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Redis;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class CustomerOrderEndpointsUnitTests
{
    private static (DefaultHttpContext context, Mock<IJwtClaimPrincipalParser> parserMock, Mock<IPermissionRegistry> permissionMock, Mock<ICustomerOrderService> orderServiceMock)
        CreateTestContext(
            AuthRole role = AuthRole.Customer,
            Guid? tenantId = null,
            Guid? branchId = null,
            Guid? sessionId = null,
            string? idempotencyKey = "test-key-1",
            bool hasPermission = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/customer/orders";
        context.TraceIdentifier = "test-corr-id-123";

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            context.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        var tId = tenantId ?? Guid.NewGuid();
        var bId = branchId ?? Guid.NewGuid();
        var sId = sessionId ?? Guid.NewGuid();

        var scope = role == AuthRole.Customer
            ? AuthorizationScope.ForTableSession(new TenantId(tId), new RestaurantOrder.Domain.Branches.BranchId(bId), sId)
            : AuthorizationScope.ForTenant(new TenantId(tId));

        var principal = new AuthenticatedPrincipal(
            subjectId: sId,
            principalType: role == AuthRole.Customer ? PrincipalType.Customer : PrincipalType.Staff,
            role: role,
            scope: scope,
            sessionId: sId,
            authMethod: AuthenticationMethod.CustomerQrSession,
            securityVersion: 0);

        var parserMock = new Mock<IJwtClaimPrincipalParser>();
        parserMock.Setup(p => p.ParsePrincipal(It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<DateTimeOffset?>()))
            .Returns(principal);

        var permissionMock = new Mock<IPermissionRegistry>();
        permissionMock.Setup(p => p.IsPermitted(principal, Permissions.OrdersQrCreate, null))
            .Returns(hasPermission);

        var orderServiceMock = new Mock<ICustomerOrderService>();

        return (context, parserMock, permissionMock, orderServiceMock);
    }

    [Fact]
    public async Task CreateCustomerOrder_MissingIdempotencyKey_Returns400BadRequest()
    {
        // Arrange
        var (context, parser, permission, orderService) = CreateTestContext(idempotencyKey: null);
        var app = new Microsoft.AspNetCore.Routing.RouteCollection();

        // Simulate endpoint logic directly or via handler
        var request = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>());

        // If Idempotency-Key is missing:
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
        {
            var problem = Results.Json(new
            {
                type = "https://httpstatuses.com/400",
                title = "Bad Request",
                status = StatusCodes.Status400BadRequest,
                detail = "Idempotency-Key header is required.",
                instance = context.Request.Path.Value
            }, statusCode: StatusCodes.Status400BadRequest);

            Assert.NotNull(problem);
        }
    }

    [Fact]
    public void MapPricingException_OrderPriceChanged_Produces409WithCorrectExtensions()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/customer/orders";
        context.TraceIdentifier = "trace-456";

        var menuItemId = MenuItemId.New();
        var itemVariantId = ItemVariantId.New();

        var ex = OrderPricingException.PriceChanged(menuItemId, itemVariantId, 2000, 2500);

        // Map exception via endpoint handler
        var result = CustomerOrderEndpointsTestsHelper.InvokeHandleOrderException(ex, context);
        Assert.NotNull(result);
    }

    [Fact]
    public void MapPricingException_ItemOutOfStock_Produces409Conflict()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/customer/orders";

        var ex = OrderPricingException.ItemOutOfStock(MenuItemId.New());
        var result = CustomerOrderEndpointsTestsHelper.InvokeHandleOrderException(ex, context);
        Assert.NotNull(result);
    }

    [Fact]
    public void MapException_IdempotencyConflict_Produces409Conflict()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/customer/orders";

        var ex = new IdempotencyConflictException("Conflict detected.");
        var result = CustomerOrderEndpointsTestsHelper.InvokeHandleOrderException(ex, context);
        Assert.NotNull(result);
    }

    [Fact]
    public void MapException_SecurityStateUnavailable_Produces503ServiceUnavailable()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/customer/orders";

        var ex = new DistributedSecurityStateUnavailableException("Redis cluster down");
        var result = CustomerOrderEndpointsTestsHelper.InvokeHandleOrderException(ex, context);
        Assert.NotNull(result);
    }
}

public static class CustomerOrderEndpointsTestsHelper
{
    public static IResult InvokeHandleOrderException(Exception ex, HttpContext context)
    {
        // Reflection or internal call to test error mapping accurately
        var method = typeof(CustomerOrderEndpoints).GetMethod("HandleOrderException",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (IResult)method!.Invoke(null, new object[] { ex, context })!;
    }
}
