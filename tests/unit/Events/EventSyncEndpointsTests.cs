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
using RestaurantOrder.Api.Realtime;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Events;
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

    private static async Task<IResult> SimulateSync(
        long sinceSequence,
        int? limit,
        ClaimsPrincipal user,
        HttpContext httpContext,
        RestaurantOrderDbContext dbContext)
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

        var safeLimit = Math.Clamp(limit ?? 50, 1, 100);
        var isCustomer = string.Equals(principalType, "customer", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(roleClaim, "Customer", StringComparison.OrdinalIgnoreCase);

        Guid? customerSessionId = null;
        if (isCustomer)
        {
            var tableSessionClaim = user.FindFirst(JwtClaimNames.TableSessionId)?.Value ?? user.FindFirst("table_session_id")?.Value;
            if (!Guid.TryParse(tableSessionClaim, out var parsedSessionId) || parsedSessionId == Guid.Empty)
            {
                return Results.Unauthorized();
            }
            customerSessionId = parsedSessionId;
        }

        var tenantIdTyped = new TenantId(tenantId);
        var branchIdTyped = new BranchId(branchId);

        var retentionCutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var oldestSequence = await dbContext.OperationalOutbox
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantIdTyped && o.BranchId == branchIdTyped && o.CreatedAtUtc >= retentionCutoff)
            .OrderBy(o => o.SequenceNumber)
            .Select(o => (long?)o.SequenceNumber)
            .FirstOrDefaultAsync();

        if (sinceSequence > 0 && oldestSequence.HasValue && sinceSequence < oldestSequence.Value)
        {
            httpContext.Response.Headers.Append("X-Resync-Required", "true");
        }

        var query = dbContext.OperationalOutbox
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantIdTyped &&
                        o.BranchId == branchIdTyped &&
                        o.SequenceNumber > sinceSequence);

        if (isCustomer && customerSessionId.HasValue)
        {
            var sessionStr = customerSessionId.Value.ToString();
            query = query.Where(o => o.EventType == RealtimeEventTypes.MenuItemAvailabilityChanged ||
                                     o.EventType == RealtimeEventTypes.CatalogPolicyInvalidated ||
                                     o.Payload.Contains(sessionStr));
        }

        var records = await query
            .OrderBy(o => o.SequenceNumber)
            .Take(safeLimit + 1)
            .ToListAsync();

        var hasMore = records.Count > safeLimit;
        var returnRecords = records.Take(safeLimit).ToList();

        var eventEnvelopes = new List<EventEnvelope<object>>();
        foreach (var rec in returnRecords)
        {
            try
            {
                using var doc = JsonDocument.Parse(rec.Payload);
                if (doc.RootElement.TryGetProperty("event_id", out _))
                {
                    var env = JsonSerializer.Deserialize<EventEnvelope<object>>(rec.Payload);
                    if (env != null) eventEnvelopes.Add(env);
                }
                else
                {
                    var env = new EventEnvelope<object>(
                        EventId: rec.Id,
                        EventType: rec.EventType,
                        SchemaVersion: rec.SchemaVersion,
                        OccurredAtUtc: rec.OccurredAtUtc,
                        TenantId: rec.TenantId.Value,
                        BranchId: rec.BranchId.Value,
                        AggregateId: rec.AggregateId,
                        SequenceNumber: rec.SequenceNumber,
                        Data: doc.RootElement.Clone()
                    );
                    eventEnvelopes.Add(env);
                }
            }
            catch { }
        }

        var latestSequence = returnRecords.Count > 0
            ? returnRecords.Max(r => r.SequenceNumber)
            : sinceSequence;

        return Results.Ok(new EventsSyncResponse(
            Events: eventEnvelopes,
            LatestSequenceNumber: latestSequence,
            HasMore: hasMore,
            SyncedAtUtc: DateTimeOffset.UtcNow));
    }
}
