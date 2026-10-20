using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Api.Realtime;

public static class EventSyncEndpoints
{
    public static IEndpointRouteBuilder MapEventSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/events/sync", async (
            long since_sequence,
            int? limit,
            ClaimsPrincipal user,
            HttpContext httpContext,
            RestaurantOrderDbContext dbContext,
            CancellationToken ct) =>
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

            // Check retention window (24 hours)
            var retentionCutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var oldestSequence = await dbContext.OperationalOutbox
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantIdTyped && o.BranchId == branchIdTyped && o.CreatedAtUtc >= retentionCutoff)
                .OrderBy(o => o.SequenceNumber)
                .Select(o => (long?)o.SequenceNumber)
                .FirstOrDefaultAsync(ct);

            if (since_sequence > 0 && oldestSequence.HasValue && since_sequence < oldestSequence.Value)
            {
                httpContext.Response.Headers.Append("X-Resync-Required", "true");
            }

            // Query matching outbox records
            var query = dbContext.OperationalOutbox
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantIdTyped &&
                            o.BranchId == branchIdTyped &&
                            o.SequenceNumber > since_sequence);

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
                .ToListAsync(ct);

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
                        if (env != null)
                        {
                            eventEnvelopes.Add(env);
                        }
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
                catch
                {
                    // Ignore malformed payload in sync
                }
            }

            var latestSequence = returnRecords.Count > 0
                ? returnRecords.Max(r => r.SequenceNumber)
                : since_sequence;

            return Results.Ok(new EventsSyncResponse(
                Events: eventEnvelopes,
                LatestSequenceNumber: latestSequence,
                HasMore: hasMore,
                SyncedAtUtc: DateTimeOffset.UtcNow));
        })
        .RequireAuthorization()
        .WithName("SyncEvents")
        .WithTags("Events");

        return endpoints;
    }
}
