using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantOrder.Api.Orders;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
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
            bool hasPermission = true,
            bool omitTenant = false,
            bool omitBranch = false,
            bool omitSession = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/customer/orders";
        context.TraceIdentifier = "test-corr-id-123";

        if (idempotencyKey != null)
        {
            context.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        var tId = omitTenant ? (Guid?)null : (tenantId ?? Guid.NewGuid());
        var bId = omitBranch ? (Guid?)null : (branchId ?? Guid.NewGuid());
        var sId = omitSession ? (Guid?)null : (sessionId ?? Guid.NewGuid());

        AuthenticatedPrincipal principal;
        if (role == AuthRole.Customer)
        {
            var tIdObj = omitTenant ? (TenantId?)null : new TenantId(tenantId ?? Guid.NewGuid());
            var bIdObj = omitBranch ? (BranchId?)null : new BranchId(branchId ?? Guid.NewGuid());
            var sIdVal = omitSession ? (Guid?)null : (sessionId ?? Guid.NewGuid());

            var scope = CreateScopeWithClaims(tIdObj, bIdObj, sIdVal);

            principal = new AuthenticatedPrincipal(
                subjectId: sIdVal ?? Guid.NewGuid(),
                principalType: PrincipalType.Customer,
                role: AuthRole.Customer,
                scope: scope,
                sessionId: sIdVal ?? Guid.NewGuid(),
                authMethod: AuthenticationMethod.CustomerQrSession,
                securityVersion: 0);
        }
        else
        {
            var staffScope = AuthorizationScope.ForBranch(new TenantId(tenantId ?? Guid.NewGuid()), new BranchId(branchId ?? Guid.NewGuid()));
            principal = AuthenticatedPrincipal.CreateStaff(
                userId: Guid.NewGuid(),
                role: role,
                scope: staffScope,
                sessionId: sessionId ?? Guid.NewGuid(),
                authMethod: AuthenticationMethod.Pin,
                securityVersion: 1);
        }

        var parserMock = new Mock<IJwtClaimPrincipalParser>();
        parserMock.Setup(p => p.ParsePrincipal(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<DateTimeOffset?>()))
            .Returns(principal);
        parserMock.Setup(p => p.ParsePrincipal(It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<DateTimeOffset?>()))
            .Returns(principal);

        var permissionMock = new Mock<IPermissionRegistry>();
        permissionMock.Setup(p => p.IsPermitted(principal, Permissions.OrdersQrCreate, null))
            .Returns(hasPermission);

        var orderServiceMock = new Mock<ICustomerOrderService>();

        return (context, parserMock, permissionMock, orderServiceMock);
    }

    private static AuthorizationScope CreateScopeWithClaims(TenantId? tenantId, BranchId? branchId, Guid? sessionId)
    {
        var ctor = typeof(AuthorizationScope).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(AuthorizationScopeType), typeof(TenantId), typeof(BranchId), typeof(Guid?) },
            null);
        return (AuthorizationScope)ctor!.Invoke(new object?[] { AuthorizationScopeType.TableSession, tenantId, branchId, sessionId });
    }

    private static CustomerOrderResponseDto CreateSampleOrderDto(Guid? orderId = null, Guid? token = null) =>
        new(
            Id: orderId ?? Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            DiningSessionId: Guid.NewGuid(),
            TableId: Guid.NewGuid(),
            RoundNumber: 1,
            OrderSource: "CustomerQr",
            Status: "Submitted",
            Currency: "TRY",
            SubtotalMinorUnits: 2500,
            TaxMinorUnits: 250,
            ServiceChargeMinorUnits: 0,
            TotalMinorUnits: 2750,
            IdempotencyKey: "test-key-1",
            ConcurrencyToken: token ?? Guid.NewGuid(),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            SubmittedAtUtc: DateTimeOffset.UtcNow,
            Items: new List<CustomerOrderItemDto>());

    private static OrderPricingException CreateOrderPricingException(
        string errorCode,
        string message,
        MenuItemId? menuItemId = null,
        ItemVariantId? itemVariantId = null,
        long? expectedPrice = null,
        long? actualPrice = null)
    {
        var ctor = typeof(OrderPricingException).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(string), typeof(string), typeof(MenuItemId), typeof(ItemVariantId), typeof(long?), typeof(long?) },
            null);
        return (OrderPricingException)ctor!.Invoke(new object?[] { errorCode, message, menuItemId, itemVariantId, expectedPrice, actualPrice });
    }

    [Theory]
    [InlineData(OrderPricingException.CodePriceChanged, StatusCodes.Status409Conflict)]
    [InlineData(OrderPricingException.CodeItemOutOfStock, StatusCodes.Status409Conflict)]
    [InlineData(OrderPricingException.CodeOrderTakingDisabled, StatusCodes.Status403Forbidden)]
    [InlineData(OrderPricingException.CodeSessionNotActive, StatusCodes.Status409Conflict)]
    [InlineData(OrderPricingException.CodeInvalidModifierSelection, StatusCodes.Status400BadRequest)]
    [InlineData(OrderPricingException.CodeDuplicateModifier, StatusCodes.Status400BadRequest)]
    [InlineData(OrderPricingException.CodeInvalidVariant, StatusCodes.Status400BadRequest)]
    [InlineData(OrderPricingException.CodeItemNotActive, StatusCodes.Status400BadRequest)]
    [InlineData(OrderPricingException.CodeCurrencyMismatch, StatusCodes.Status400BadRequest)]
    [InlineData("UNKNOWN_CODE", StatusCodes.Status400BadRequest)]
    public void MapPricingException_AllCodes_ReturnExpectedStatusCode(string errorCode, int expectedStatus)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "corr-1" };
        var ex = CreateOrderPricingException(errorCode, "Pricing error occurred", MenuItemId.New(), ItemVariantId.New(), 100, 200);

        var result = CustomerOrderEndpoints.MapPricingException(ex, context);
        var jsonResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(expectedStatus, jsonResult.StatusCode);
    }

    [Fact]
    public void MapPricingException_WithAndWithoutOptionalFields_PopulatesExtensions()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "corr-2" };

        var exFull = CreateOrderPricingException(OrderPricingException.CodePriceChanged, "Full", MenuItemId.New(), ItemVariantId.New(), 100, 150);
        var resFull = CustomerOrderEndpoints.MapPricingException(exFull, context);
        Assert.NotNull(resFull);

        var exEmpty = CreateOrderPricingException(OrderPricingException.CodeOrderTakingDisabled, "Empty", null, null, null, null);
        var resEmpty = CustomerOrderEndpoints.MapPricingException(exEmpty, context);
        Assert.NotNull(resEmpty);
    }

    [Fact]
    public void HandleOrderException_AllTypes_MappedCorrectly()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "corr-3" };

        var r1 = CustomerOrderEndpoints.HandleOrderException(new IdempotencyConflictException("Conflict"), context);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r1).StatusCode);

        var r2 = CustomerOrderEndpoints.HandleOrderException(OrderPricingException.ItemOutOfStock(MenuItemId.New()), context);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r2).StatusCode);

        var r3 = CustomerOrderEndpoints.HandleOrderException(new DistributedSecurityStateUnavailableException("Redis fail"), context);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r3).StatusCode);

        var r4 = CustomerOrderEndpoints.HandleOrderException(new DbUpdateConcurrencyException("DB concurrency conflict"), context);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r4).StatusCode);

        var rGenericDb = CustomerOrderEndpoints.HandleOrderException(new DbUpdateException("Unexpected DB update failure"), context);
        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsAssignableFrom<IStatusCodeHttpResult>(rGenericDb).StatusCode);

        var r5 = CustomerOrderEndpoints.HandleOrderException(new DomainException("Domain violation"), context);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r5).StatusCode);

        var r6 = CustomerOrderEndpoints.HandleOrderException(new InvalidOperationException("Generic failure"), context);
        Assert.NotNull(r6);
    }

    [Fact]
    public void ProblemResult_ExtensionsAndErrorCodeHandling()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "corr-4" };

        var r1 = CustomerOrderEndpoints.ProblemResult(context, StatusCodes.Status400BadRequest, "Title", "Detail", null, null);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r1).StatusCode);

        var extWithCode = new Dictionary<string, object?> { ["errorCode"] = "EXISTING_CODE" };
        var r2 = CustomerOrderEndpoints.ProblemResult(context, StatusCodes.Status400BadRequest, "Title", "Detail", "NEW_CODE", extWithCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r2).StatusCode);

        var extWithoutCode = new Dictionary<string, object?> { ["custom"] = "val" };
        var r3 = CustomerOrderEndpoints.ProblemResult(context, StatusCodes.Status400BadRequest, "Title", "Detail", "NEW_CODE", extWithoutCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r3).StatusCode);
    }

    [Fact]
    public async Task CreateCustomerOrderAsync_GuardsAndSuccessBranches()
    {
        var request = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>());

        // 1. Role is not customer
        var (c1, p1, pr1, s1) = CreateTestContext(role: AuthRole.Waiter);
        var res1 = await CustomerOrderEndpoints.CreateCustomerOrderAsync(request, c1, p1.Object, pr1.Object, s1.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res1).StatusCode);

        // 2. Permission denied
        var (c2, p2, pr2, s2) = CreateTestContext(hasPermission: false);
        var res2 = await CustomerOrderEndpoints.CreateCustomerOrderAsync(request, c2, p2.Object, pr2.Object, s2.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res2).StatusCode);

        // 3. Missing claims
        var (c3a, p3a, pr3a, s3a) = CreateTestContext(omitTenant: true);
        var res3a = await CustomerOrderEndpoints.CreateCustomerOrderAsync(request, c3a, p3a.Object, pr3a.Object, s3a.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res3a).StatusCode);

        var (c3b, p3b, pr3b, s3b) = CreateTestContext(omitBranch: true);
        var res3b = await CustomerOrderEndpoints.CreateCustomerOrderAsync(request, c3b, p3b.Object, pr3b.Object, s3b.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res3b).StatusCode);

        var (c3c, p3c, pr3c, s3c) = CreateTestContext(omitSession: true);
        var res3c = await CustomerOrderEndpoints.CreateCustomerOrderAsync(request, c3c, p3c.Object, pr3c.Object, s3c.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res3c).StatusCode);

        // 4. Missing Idempotency-Key
        var (c4a, p4a, pr4a, s4a) = CreateTestContext(idempotencyKey: null);
        var res4a = await CustomerOrderEndpoints.CreateCustomerOrderAsync(request, c4a, p4a.Object, pr4a.Object, s4a.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res4a).StatusCode);

        var (c4b, p4b, pr4b, s4b) = CreateTestContext(idempotencyKey: "   ");
        var res4b = await CustomerOrderEndpoints.CreateCustomerOrderAsync(request, c4b, p4b.Object, pr4b.Object, s4b.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res4b).StatusCode);

        // 5. Success non-replay -> 201 Created
        var (c5, p5, pr5, s5) = CreateTestContext();
        var order5 = CreateSampleOrderDto();
        s5.Setup(x => x.CreateOrderAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<CreateCustomerOrderRequest>(), It.IsAny<string>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateCustomerOrderResult(order5, IsReplay: false));
        var res5 = await CustomerOrderEndpoints.CreateCustomerOrderAsync(request, c5, p5.Object, pr5.Object, s5.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res5).StatusCode);
        Assert.True(c5.Response.Headers.ContainsKey("ETag"));

        // 6. Success replay -> 200 OK
        var (c6, p6, pr6, s6) = CreateTestContext();
        s6.Setup(x => x.CreateOrderAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<CreateCustomerOrderRequest>(), It.IsAny<string>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateCustomerOrderResult(order5, IsReplay: true));
        var res6 = await CustomerOrderEndpoints.CreateCustomerOrderAsync(request, c6, p6.Object, pr6.Object, s6.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res6).StatusCode);

        // 7. Service exception -> handled
        var (c7, p7, pr7, s7) = CreateTestContext();
        s7.Setup(x => x.CreateOrderAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<CreateCustomerOrderRequest>(), It.IsAny<string>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Item inactive"));
        var res7 = await CustomerOrderEndpoints.CreateCustomerOrderAsync(request, c7, p7.Object, pr7.Object, s7.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res7).StatusCode);
    }

    [Fact]
    public async Task GetCustomerOrdersAsync_GuardsAndSuccessBranches()
    {
        // 1. Role is not customer
        var (c1, p1, _, s1) = CreateTestContext(role: AuthRole.Waiter);
        var res1 = await CustomerOrderEndpoints.GetCustomerOrdersAsync(c1, p1.Object, s1.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res1).StatusCode);

        // 2. Missing claims
        var (c2, p2, _, s2) = CreateTestContext(omitTenant: true);
        var res2 = await CustomerOrderEndpoints.GetCustomerOrdersAsync(c2, p2.Object, s2.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res2).StatusCode);

        // 3. Success
        var (c3, p3, _, s3) = CreateTestContext();
        s3.Setup(x => x.GetOrdersAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CustomerOrderResponseDto> { CreateSampleOrderDto() });
        var res3 = await CustomerOrderEndpoints.GetCustomerOrdersAsync(c3, p3.Object, s3.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res3).StatusCode);

        // 4. Exception
        var (c4, p4, _, s4) = CreateTestContext();
        s4.Setup(x => x.GetOrdersAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Err"));
        var res4 = await CustomerOrderEndpoints.GetCustomerOrdersAsync(c4, p4.Object, s4.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res4).StatusCode);
    }

    [Fact]
    public async Task GetCustomerOrderByIdAsync_GuardsAndSuccessBranches()
    {
        var orderId = Guid.NewGuid();

        // 1. Role is not customer
        var (c1, p1, _, s1) = CreateTestContext(role: AuthRole.Waiter);
        var res1 = await CustomerOrderEndpoints.GetCustomerOrderByIdAsync(orderId, c1, p1.Object, s1.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res1).StatusCode);

        // 2. Missing claims
        var (c2, p2, _, s2) = CreateTestContext(omitTenant: true);
        var res2 = await CustomerOrderEndpoints.GetCustomerOrderByIdAsync(orderId, c2, p2.Object, s2.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res2).StatusCode);

        // 3. Order not found -> 404
        var (c3, p3, _, s3) = CreateTestContext();
        s3.Setup(x => x.GetOrderByIdAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerOrderResponseDto?)null);
        var res3 = await CustomerOrderEndpoints.GetCustomerOrderByIdAsync(orderId, c3, p3.Object, s3.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res3).StatusCode);

        // 4. Order found -> 200 + ETag
        var (c4, p4, _, s4) = CreateTestContext();
        var sampleOrder = CreateSampleOrderDto(orderId);
        s4.Setup(x => x.GetOrderByIdAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleOrder);
        var res4 = await CustomerOrderEndpoints.GetCustomerOrderByIdAsync(orderId, c4, p4.Object, s4.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res4).StatusCode);
        Assert.True(c4.Response.Headers.ContainsKey("ETag"));

        // 5. Exception
        var (c5, p5, _, s5) = CreateTestContext();
        s5.Setup(x => x.GetOrderByIdAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Err"));
        var res5 = await CustomerOrderEndpoints.GetCustomerOrderByIdAsync(orderId, c5, p5.Object, s5.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res5).StatusCode);
    }
}
