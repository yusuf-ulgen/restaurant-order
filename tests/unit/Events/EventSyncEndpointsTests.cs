using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantOrder.Api.Realtime;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

public class EventSyncEndpointsTests
{
    private static RestaurantOrderDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseInMemoryDatabase(databaseName: $"sync_test_{Guid.NewGuid():N}")
            .Options;

        return new RestaurantOrderDbContext(options);
    }

    [Fact]
    public async Task SyncEvents_MissingTenantOrBranchClaim_ReturnsUnauthorized()
    {
        using var db = CreateInMemoryDbContext();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var httpContext = new DefaultHttpContext();

        // Calling through helper or simulated endpoint invocation
        var tenantId = Guid.Empty;
        var result = await SimulateSync(0, null, principal, httpContext, db);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task SyncEvents_Customer_FiltersToOwnSessionAndCatalogEvents()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var mySessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // 1. My session order created
        var myOrderMsg = OperationalOutboxMessage.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            "agg-1",
            RealtimeEventTypes.OrderCreated,
            $"{{\"dining_session_id\":\"{mySessionId}\",\"order_id\":\"{Guid.NewGuid()}\"}}",
            "idem-1",
            now,
            now);
        typeof(OperationalOutboxMessage).GetProperty("SequenceNumber")!.SetValue(myOrderMsg, 10L);

        // 2. Other session order created
        var otherOrderMsg = OperationalOutboxMessage.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            "agg-2",
            RealtimeEventTypes.OrderCreated,
            $"{{\"dining_session_id\":\"{otherSessionId}\",\"order_id\":\"{Guid.NewGuid()}\"}}",
            "idem-2",
            now,
            now);
        typeof(OperationalOutboxMessage).GetProperty("SequenceNumber")!.SetValue(otherOrderMsg, 11L);

        // 3. Catalog 86 event (accessible to all sessions)
        var catalogMsg = OperationalOutboxMessage.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            "agg-3",
            RealtimeEventTypes.MenuItemAvailabilityChanged,
            "{\"item_id\":\"item-1\",\"is_available\":false}",
            "idem-3",
            now,
            now);
        typeof(OperationalOutboxMessage).GetProperty("SequenceNumber")!.SetValue(catalogMsg, 12L);

        db.OperationalOutbox.AddRange(myOrderMsg, otherOrderMsg, catalogMsg);
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(JwtClaimNames.PrincipalType, "customer"),
            new Claim(JwtClaimNames.TableSessionId, mySessionId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext();

        var result = await SimulateSync(sinceSequence: 0, limit: 50, principal, httpContext, db);

        var okResult = Assert.IsType<Ok<EventsSyncResponse>>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        // Customer must only see 2 events: my order and catalog change. Other session order must NOT be visible.
        Assert.Equal(2, response.Events.Count);
        Assert.Contains(response.Events, e => e.EventType == RealtimeEventTypes.OrderCreated);
        Assert.Contains(response.Events, e => e.EventType == RealtimeEventTypes.MenuItemAvailabilityChanged);
        Assert.DoesNotContain(response.Events, e => e.AggregateId == "agg-2");
    }

    [Fact]
    public async Task SyncEvents_Staff_SeesAllBranchEvents()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var msg1 = OperationalOutboxMessage.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            "agg-1",
            RealtimeEventTypes.OrderCreated,
            "{\"data\":\"test1\"}",
            "idem-1",
            now,
            now);
        typeof(OperationalOutboxMessage).GetProperty("SequenceNumber")!.SetValue(msg1, 1L);

        var msg2 = OperationalOutboxMessage.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            "agg-2",
            RealtimeEventTypes.TableServiceRequested,
            "{\"data\":\"test2\"}",
            "idem-2",
            now,
            now);
        typeof(OperationalOutboxMessage).GetProperty("SequenceNumber")!.SetValue(msg2, 2L);

        db.OperationalOutbox.AddRange(msg1, msg2);
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Waiter"),
            new Claim(JwtClaimNames.PrincipalType, "staff")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext();

        var result = await SimulateSync(sinceSequence: 0, limit: 50, principal, httpContext, db);

        var okResult = Assert.IsType<Ok<EventsSyncResponse>>(result);
        var response = okResult.Value;
        Assert.NotNull(response);
        Assert.Equal(2, response.Events.Count);
        Assert.Equal(2L, response.LatestSequenceNumber);
    }

    [Fact]
    public async Task SyncEvents_WhenSinceSequenceOlderThanRetention_AppendsResyncHeader()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var msg1 = OperationalOutboxMessage.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            "agg-100",
            RealtimeEventTypes.OrderCreated,
            "{\"data\":\"test\"}",
            "idem-100",
            now,
            now);
        typeof(OperationalOutboxMessage).GetProperty("SequenceNumber")!.SetValue(msg1, 100L);

        db.OperationalOutbox.Add(msg1);
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Waiter"),
            new Claim(JwtClaimNames.PrincipalType, "staff")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext();

        // Client asks for sinceSequence 10, but oldest available in DB is 100
        var result = await SimulateSync(sinceSequence: 10, limit: 50, principal, httpContext, db);

        var okResult = Assert.IsType<Ok<EventsSyncResponse>>(result);
        Assert.Equal("true", httpContext.Response.Headers["X-Resync-Required"]);
    }

    [Fact]
    public async Task SyncEvents_Customer_WhenSessionClosed_ReturnsUnauthorizedAndSessionClosedHeader()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(JwtClaimNames.PrincipalType, "customer"),
            new Claim(JwtClaimNames.TableSessionId, sessionId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext();

        var sessionValidatorMock = new Mock<ICustomerSessionValidator>();
        sessionValidatorMock
            .Setup(s => s.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await SimulateSync(0, 50, principal, httpContext, db, sessionValidator: sessionValidatorMock.Object);

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Equal("true", httpContext.Response.Headers["X-Session-Closed"]);
    }

    [Fact]
    public async Task SyncEvents_RestaurantAdmin_MissingBranchSelection_ReturnsBadRequest()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(ClaimTypes.Role, "RestaurantAdmin"),
            new Claim(JwtClaimNames.PrincipalType, "staff")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext();

        var result = await SimulateSync(0, 50, principal, httpContext, db);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusResult.StatusCode);
    }

    [Fact]
    public async Task SyncEvents_RestaurantAdmin_InvalidBranchForTenant_ReturnsForbid()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(ClaimTypes.Role, "RestaurantAdmin"),
            new Claim(JwtClaimNames.PrincipalType, "staff")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?branchId={branchId}");

        var branchValidatorMock = new Mock<IBranchScopeValidator>();
        branchValidatorMock
            .Setup(b => b.ValidateBranchBelongsToTenantAsync(tenantId, branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await SimulateSync(0, 50, principal, httpContext, db, branchValidator: branchValidatorMock.Object);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task SyncEvents_KitchenRole_FiltersOutIrrelevantEvents()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var orderMsg = OperationalOutboxMessage.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            "agg-1",
            RealtimeEventTypes.OrderCreated,
            "{\"data\":\"order1\"}",
            "idem-1",
            now,
            now);
        typeof(OperationalOutboxMessage).GetProperty("SequenceNumber")!.SetValue(orderMsg, 1L);

        var serviceMsg = OperationalOutboxMessage.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            "agg-2",
            RealtimeEventTypes.TableServiceRequested,
            "{\"data\":\"service1\"}",
            "idem-2",
            now,
            now);
        typeof(OperationalOutboxMessage).GetProperty("SequenceNumber")!.SetValue(serviceMsg, 2L);

        db.OperationalOutbox.AddRange(orderMsg, serviceMsg);
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Kitchen"),
            new Claim(JwtClaimNames.PrincipalType, "staff")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext();

        var result = await SimulateSync(0, 50, principal, httpContext, db);

        var okResult = Assert.IsType<Ok<EventsSyncResponse>>(result);
        Assert.NotNull(okResult.Value);
        Assert.Single(okResult.Value.Events);
        Assert.Equal(RealtimeEventTypes.OrderCreated, okResult.Value.Events[0].EventType);
    }

    [Fact]
    public async Task SyncEvents_CorruptPayload_SetsResyncHeaderAndStopsAdvancing()
    {
        using var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var corruptMsg = OperationalOutboxMessage.Create(
            new TenantId(tenantId),
            new BranchId(branchId),
            "agg-bad",
            RealtimeEventTypes.OrderCreated,
            "NOT_VALID_JSON{{{",
            "idem-bad",
            now,
            now);
        typeof(OperationalOutboxMessage).GetProperty("SequenceNumber")!.SetValue(corruptMsg, 5L);

        db.OperationalOutbox.Add(corruptMsg);
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(JwtClaimNames.TenantId, tenantId.ToString()),
            new Claim(JwtClaimNames.BranchId, branchId.ToString()),
            new Claim(ClaimTypes.Role, "Waiter"),
            new Claim(JwtClaimNames.PrincipalType, "staff")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext();

        var result = await SimulateSync(0, 50, principal, httpContext, db);

        var okResult = Assert.IsType<Ok<EventsSyncResponse>>(result);
        Assert.NotNull(okResult.Value);
        Assert.Empty(okResult.Value.Events);
        Assert.Equal("true", httpContext.Response.Headers["X-Resync-Required"]);
    }

    private static async Task<IResult> SimulateSync(
        long sinceSequence,
        int? limit,
        ClaimsPrincipal user,
        HttpContext httpContext,
        RestaurantOrderDbContext dbContext,
        ICustomerSessionValidator? sessionValidator = null,
        IBranchScopeValidator? branchValidator = null)
    {
        var sessVal = sessionValidator ?? Mock.Of<ICustomerSessionValidator>(v =>
            v.ValidateSessionActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true));

        var brVal = branchValidator ?? Mock.Of<IBranchScopeValidator>(v =>
            v.ValidateBranchBelongsToTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true));

        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

        return await EventSyncEndpoints.SyncEventsHandlerAsync(
            sinceSequence,
            limit,
            user,
            httpContext,
            dbContext,
            sessVal,
            brVal,
            loggerFactory);
    }
}
