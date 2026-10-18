using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantOrder.Api.Orders;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Orders.Pricing;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class StaffOrderEndpointsUnitTests
{
    private static (DefaultHttpContext context, Mock<IJwtClaimPrincipalParser> parserMock, Mock<ITenantContext> tenantContextMock, Mock<IStaffOrderService> orderServiceMock)
        CreateStaffTestContext(
            AuthRole role = AuthRole.Waiter,
            Guid? tenantId = null,
            Guid? branchId = null,
            string? idempotencyKey = "staff-key-1",
            Guid? ifMatch = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/floor/branches/" + Guid.NewGuid() + "/sessions/" + Guid.NewGuid() + "/orders";
        context.TraceIdentifier = "staff-corr-id-456";

        if (idempotencyKey != null)
        {
            context.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        if (ifMatch.HasValue)
        {
            context.Request.Headers.IfMatch = $"\"{ifMatch.Value:D}\"";
        }

        var tId = tenantId ?? Guid.NewGuid();
        var bId = branchId ?? Guid.NewGuid();
        var sId = Guid.NewGuid();

        var scope = AuthorizationScope.ForBranch(new TenantId(tId), new BranchId(bId));
        var principal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: role,
            scope: scope,
            sessionId: sId,
            authMethod: AuthenticationMethod.Pin,
            securityVersion: 1);

        var parserMock = new Mock<IJwtClaimPrincipalParser>();
        parserMock.Setup(p => p.ParsePrincipal(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<DateTimeOffset?>()))
            .Returns(principal);
        parserMock.Setup(p => p.ParsePrincipal(It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<DateTimeOffset?>()))
            .Returns(principal);

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.TenantId).Returns(tId);
        tenantContextMock.Setup(tc => tc.RequireTenantId()).Returns(tId);

        var orderServiceMock = new Mock<IStaffOrderService>();

        return (context, parserMock, tenantContextMock, orderServiceMock);
    }

    private static StaffOrderResponseDto CreateSampleStaffOrderDto(Guid? orderId = null, Guid? token = null) =>
        new(
            Id: orderId ?? Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            DiningSessionId: Guid.NewGuid(),
            TableId: Guid.NewGuid(),
            RoundNumber: 1,
            OrderSource: "StaffManual",
            Status: "Submitted",
            Currency: "TRY",
            SubtotalMinorUnits: 4000,
            TaxMinorUnits: 400,
            ServiceChargeMinorUnits: 0,
            TotalMinorUnits: 4400,
            IdempotencyKey: "staff-key-1",
            ConcurrencyToken: token ?? Guid.NewGuid(),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            SubmittedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: null,
            CancellationReason: null,
            CancelledByActorId: null,
            Items: new List<StaffOrderItemDto>());

    [Fact]
    public void HandleException_AllBranches_MappedCorrectly()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "staff-corr-test" };

        var r1 = StaffOrderEndpoints.HandleException(OrderPricingException.OrderTakingDisabled(), context);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r1).StatusCode);

        var r2 = StaffOrderEndpoints.HandleException(
            OrderPricingException.PriceChanged(MenuItemId.New(), null, 100, 200), context);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r2).StatusCode);

        var r3 = StaffOrderEndpoints.HandleException(new IdempotencyConflictException("Conflict"), context);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r3).StatusCode);

        var r4 = StaffOrderEndpoints.HandleException(new ResourceNotFoundException("Not found"), context);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r4).StatusCode);

        var r5 = StaffOrderEndpoints.HandleException(new ConcurrencyPreconditionException("Missing token"), context);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r5).StatusCode);

        var r6 = StaffOrderEndpoints.HandleException(new ConcurrencyConflictException("Stale"), context);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r6).StatusCode);

        var r7 = StaffOrderEndpoints.HandleException(new InvalidAuthorizationScopeException("Forbidden"), context);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r7).StatusCode);

        var r8 = StaffOrderEndpoints.HandleException(new DomainException("Domain error"), context);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r8).StatusCode);

        var r9 = StaffOrderEndpoints.HandleException(new DbUpdateConcurrencyException("DB concurrency conflict"), context);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r9).StatusCode);

        var rGenericDb = StaffOrderEndpoints.HandleException(new DbUpdateException("DB unexpected failure"), context);
        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsAssignableFrom<IStatusCodeHttpResult>(rGenericDb).StatusCode);

        var r10 = StaffOrderEndpoints.HandleException(new InvalidOperationException("Generic"), context);
        Assert.NotNull(r10);
    }

    [Fact]
    public async Task CreateStaffOrderAsync_IdempotencyAndResultBranches()
    {
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new CreateStaffOrderRequest(new List<StaffOrderItemRequest>());

        // 1. Missing Idempotency-Key
        var (c1, p1, tc1, s1) = CreateStaffTestContext(idempotencyKey: null);
        var res1 = await StaffOrderEndpoints.CreateStaffOrderAsync(branchId, sessionId, request, c1, p1.Object, tc1.Object, s1.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res1).StatusCode);

        // 2. Whitespace Idempotency-Key
        var (c2, p2, tc2, s2) = CreateStaffTestContext(idempotencyKey: "   ");
        var res2 = await StaffOrderEndpoints.CreateStaffOrderAsync(branchId, sessionId, request, c2, p2.Object, tc2.Object, s2.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res2).StatusCode);

        // 3. Success non-replay -> 201 Created
        var (c3, p3, tc3, s3) = CreateStaffTestContext();
        var order3 = CreateSampleStaffOrderDto();
        s3.Setup(x => x.CreateStaffOrderAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<CreateStaffOrderRequest>(), It.IsAny<string>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateStaffOrderResult(order3, IsReplay: false));
        var res3 = await StaffOrderEndpoints.CreateStaffOrderAsync(branchId, sessionId, request, c3, p3.Object, tc3.Object, s3.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res3).StatusCode);
        Assert.True(c3.Response.Headers.ContainsKey("ETag"));

        // 4. Success replay -> 200 OK
        var (c4, p4, tc4, s4) = CreateStaffTestContext();
        s4.Setup(x => x.CreateStaffOrderAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<CreateStaffOrderRequest>(), It.IsAny<string>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateStaffOrderResult(order3, IsReplay: true));
        var res4 = await StaffOrderEndpoints.CreateStaffOrderAsync(branchId, sessionId, request, c4, p4.Object, tc4.Object, s4.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res4).StatusCode);

        // 5. Exception
        var (c5, p5, tc5, s5) = CreateStaffTestContext();
        s5.Setup(x => x.CreateStaffOrderAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<CreateStaffOrderRequest>(), It.IsAny<string>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Err"));
        var res5 = await StaffOrderEndpoints.CreateStaffOrderAsync(branchId, sessionId, request, c5, p5.Object, tc5.Object, s5.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res5).StatusCode);
    }

    [Fact]
    public async Task GetSessionStaffOrdersAsync_SuccessAndException()
    {
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // 1. Success
        var (c1, p1, tc1, s1) = CreateStaffTestContext();
        s1.Setup(x => x.GetSessionOrdersAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StaffOrderResponseDto> { CreateSampleStaffOrderDto() });
        var res1 = await StaffOrderEndpoints.GetSessionStaffOrdersAsync(branchId, sessionId, c1, p1.Object, tc1.Object, s1.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res1).StatusCode);

        // 2. Exception
        var (c2, p2, tc2, s2) = CreateStaffTestContext();
        s2.Setup(x => x.GetSessionOrdersAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Err"));
        var res2 = await StaffOrderEndpoints.GetSessionStaffOrdersAsync(branchId, sessionId, c2, p2.Object, tc2.Object, s2.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res2).StatusCode);
    }

    [Fact]
    public async Task GetStaffOrderByIdAsync_SuccessAndException()
    {
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // 1. Success
        var (c1, p1, tc1, s1) = CreateStaffTestContext();
        s1.Setup(x => x.GetOrderByIdAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<OrderId>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleStaffOrderDto(orderId));
        var res1 = await StaffOrderEndpoints.GetStaffOrderByIdAsync(branchId, sessionId, orderId, c1, p1.Object, tc1.Object, s1.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res1).StatusCode);
        Assert.True(c1.Response.Headers.ContainsKey("ETag"));

        // 2. Exception
        var (c2, p2, tc2, s2) = CreateStaffTestContext();
        s2.Setup(x => x.GetOrderByIdAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<OrderId>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Order missing"));
        var res2 = await StaffOrderEndpoints.GetStaffOrderByIdAsync(branchId, sessionId, orderId, c2, p2.Object, tc2.Object, s2.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res2).StatusCode);
    }

    [Fact]
    public async Task CancelStaffOrderItemAsync_ConcurrencyTokenAndResultBranches()
    {
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var token = Guid.NewGuid();

        // 1. Concurrency token from body
        var (c1, p1, tc1, s1) = CreateStaffTestContext();
        var req1 = new CancelOrderItemRequest("Customer changed mind", ConcurrencyToken: token);
        s1.Setup(x => x.CancelOrderItemAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<OrderId>(), It.IsAny<OrderItemId>(), It.IsAny<CancelOrderItemRequest>(), It.IsAny<Guid?>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleStaffOrderDto(orderId, token));
        var res1 = await StaffOrderEndpoints.CancelStaffOrderItemAsync(branchId, sessionId, orderId, itemId, req1, c1, p1.Object, tc1.Object, s1.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res1).StatusCode);
        Assert.True(c1.Response.Headers.ContainsKey("ETag"));

        // 2. Concurrency token from If-Match header
        var (c2, p2, tc2, s2) = CreateStaffTestContext(ifMatch: token);
        var req2 = new CancelOrderItemRequest("Customer changed mind", ConcurrencyToken: null);
        s2.Setup(x => x.CancelOrderItemAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<OrderId>(), It.IsAny<OrderItemId>(), It.IsAny<CancelOrderItemRequest>(), token, It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleStaffOrderDto(orderId, token));
        var res2 = await StaffOrderEndpoints.CancelStaffOrderItemAsync(branchId, sessionId, orderId, itemId, req2, c2, p2.Object, tc2.Object, s2.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res2).StatusCode);

        // 3. Exception
        var (c3, p3, tc3, s3) = CreateStaffTestContext();
        s3.Setup(x => x.CancelOrderItemAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<OrderId>(), It.IsAny<OrderItemId>(), It.IsAny<CancelOrderItemRequest>(), It.IsAny<Guid?>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("Stale token"));
        var res3 = await StaffOrderEndpoints.CancelStaffOrderItemAsync(branchId, sessionId, orderId, itemId, req1, c3, p3.Object, tc3.Object, s3.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res3).StatusCode);

        // 4. Null request branch
        var (c4, p4, tc4, s4) = CreateStaffTestContext();
        s4.Setup(x => x.CancelOrderItemAsync(It.IsAny<TenantId>(), It.IsAny<BranchId>(), It.IsAny<DiningSessionId>(), It.IsAny<OrderId>(), It.IsAny<OrderItemId>(), It.IsAny<CancelOrderItemRequest>(), It.IsAny<Guid?>(), It.IsAny<AuthenticatedPrincipal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Request null"));
        var res4 = await StaffOrderEndpoints.CancelStaffOrderItemAsync(branchId, sessionId, orderId, itemId, null!, c4, p4.Object, tc4.Object, s4.Object, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(res4).StatusCode);
    }
}
