using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Api.Realtime;

public static class EventSyncEndpoints
{
    public static IEndpointRouteBuilder MapEventSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/events/sync", (
            long since_sequence,
            int? limit,
            ClaimsPrincipal user,
            HttpContext httpContext,
            RestaurantOrderDbContext dbContext,
            ICustomerSessionValidator sessionValidator,
            IBranchScopeValidator branchValidator,
            ILoggerFactory loggerFactory,
            CancellationToken ct) => SyncEventsHandlerAsync(
                since_sequence,
                limit,
                user,
                httpContext,
                dbContext,
                sessionValidator,
                branchValidator,
                loggerFactory,
                ct))
        .RequireAuthorization()
        .WithName("SyncEvents")
        .WithTags("Events");

        return endpoints;
    }

    internal static async Task<IResult> SyncEventsHandlerAsync(
        long since_sequence,
        int? limit,
        ClaimsPrincipal user,
        HttpContext httpContext,
        RestaurantOrderDbContext dbContext,
        ICustomerSessionValidator sessionValidator,
        IBranchScopeValidator branchValidator,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default)
    {
        var logger = loggerFactory.CreateLogger("EventSyncEndpoints");
        try
        {
            var tenantClaim = user.FindFirst(JwtClaimNames.TenantId)?.Value ?? user.FindFirst("tenant_id")?.Value;
            var branchClaim = user.FindFirst(JwtClaimNames.BranchId)?.Value ?? user.FindFirst("branch_id")?.Value;
            var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
            var principalType = user.FindFirst(JwtClaimNames.PrincipalType)?.Value ?? user.FindFirst("principal_type")?.Value;

            if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantId == Guid.Empty || string.IsNullOrWhiteSpace(roleClaim))
            {
                return Results.Unauthorized();
            }

            var normalizedRole = roleClaim.Trim().ToLowerInvariant();
            Guid branchId;

            // RestaurantAdmin / SuperAdmin has tenant scope; resolves branch through query parameter or header
            if (normalizedRole is "restaurantadmin" or "restaurant_admin" or "superadmin" or "super_admin")
            {
                var branchQuery = httpContext.Request.Query["branchId"].ToString()
                    ?? httpContext.Request.Query["branch_id"].ToString()
                    ?? httpContext.Request.Headers["X-Branch-Id"].ToString();

                if (!Guid.TryParse(branchQuery, out branchId) || branchId == Guid.Empty)
                {
                    return Results.BadRequest(new { error = "Branch selection required for tenant-scoped administrator." });
                }

                var isBranchValid = await branchValidator.ValidateBranchBelongsToTenantAsync(tenantId, branchId, ct);
                if (!isBranchValid)
                {
                    return Results.Forbid();
                }
            }
            else
            {
                if (!Guid.TryParse(branchClaim, out branchId) || branchId == Guid.Empty)
                {
                    return Results.Unauthorized();
                }
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

                // Verify customer session remains active fail-closed
                var isSessionActive = await sessionValidator.ValidateSessionActiveAsync(tenantId, branchId, parsedSessionId, ct);
                if (!isSessionActive)
                {
                    httpContext.Response.Headers.Append("X-Session-Closed", "true");
                    return Results.Unauthorized();
                }

                customerSessionId = parsedSessionId;
            }

            var tenantIdTyped = new TenantId(tenantId);
            var branchIdTyped = new BranchId(branchId);

            // Establish tenant transaction for RLS protection if not already opened by middleware
            await using var tx = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction == null
                ? await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken: ct) : null;

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

            // SHARE lock waits for every in-flight writer, independent of XID allocation order.
            // New inserts wait until this short scan transaction ends. Never fall back to an unsafe cursor.
            if (dbContext.Database.IsNpgsql())
            {
                await dbContext.Database.ExecuteSqlRawAsync("LOCK TABLE tenancy.operational_outbox IN SHARE MODE", ct);
            }
            long safeWatermark = long.MaxValue;

            // Query outbox records bounded by safe watermark
            var query = dbContext.OperationalOutbox
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantIdTyped &&
                            o.BranchId == branchIdTyped &&
                            o.SequenceNumber > since_sequence &&
                            o.SequenceNumber <= safeWatermark);

            var rawRecords = await query
                .OrderBy(o => o.SequenceNumber)
                .Take(safeLimit + 1)
                .ToListAsync(ct);

            var hasMore = rawRecords.Count > safeLimit;
            var returnRecords = rawRecords.Take(safeLimit).ToList();

            var eventEnvelopes = new List<EventEnvelope<object>>();
            long lastProcessedSequence = since_sequence;

            foreach (var rec in returnRecords)
            {
                try
                {
                    using var doc = JsonDocument.Parse(rec.Payload);
                    var root = doc.RootElement;

                    // Extract typed recipient scope
                    Guid? eventSessionId = null;
                    if (root.TryGetProperty("dining_session_id", out var dsEl) && dsEl.TryGetGuid(out var parsedDsId))
                    {
                        eventSessionId = parsedDsId;
                    }
                    else if (root.TryGetProperty("data", out var dataEl) &&
                             dataEl.ValueKind == JsonValueKind.Object &&
                             dataEl.TryGetProperty("dining_session_id", out var innerDsEl) &&
                             innerDsEl.TryGetGuid(out var innerParsedDsId))
                    {
                        eventSessionId = innerParsedDsId;
                    }

                    // Enforce recipient authorization:
                    // 1. Customer: only own session and catalog events
                    if (isCustomer)
                    {
                        var isCatalogEvent = rec.EventType is RealtimeEventTypes.MenuItemAvailabilityChanged or RealtimeEventTypes.CatalogPolicyInvalidated;
                        if (!isCatalogEvent && (!eventSessionId.HasValue || eventSessionId.Value != customerSessionId!.Value))
                        {
                            lastProcessedSequence = Math.Max(lastProcessedSequence, rec.SequenceNumber);
                            continue; // Skip events belonging to other dining sessions
                        }
                    }
                    // 2. Kitchen / Bar station scoping: no floor service noise
                    else if (normalizedRole == "kitchen")
                    {
                        var isKitchenRelevant = rec.EventType is RealtimeEventTypes.OrderCreated
                            or RealtimeEventTypes.OrderItemStatusChanged
                            or RealtimeEventTypes.MenuItemAvailabilityChanged
                            or RealtimeEventTypes.CatalogPolicyInvalidated;
                        if (!isKitchenRelevant)
                        {
                            lastProcessedSequence = Math.Max(lastProcessedSequence, rec.SequenceNumber);
                            continue;
                        }
                    }
                    else if (normalizedRole == "bar")
                    {
                        var isBarRelevant = rec.EventType is RealtimeEventTypes.OrderCreated
                            or RealtimeEventTypes.OrderItemStatusChanged
                            or RealtimeEventTypes.MenuItemAvailabilityChanged
                            or RealtimeEventTypes.CatalogPolicyInvalidated;
                        if (!isBarRelevant)
                        {
                            lastProcessedSequence = Math.Max(lastProcessedSequence, rec.SequenceNumber);
                            continue;
                        }
                    }

                    if (root.TryGetProperty("event_id", out _))
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
                            Data: JsonSerializer.Deserialize<JsonElement>(rec.Payload)
                        );
                        eventEnvelopes.Add(env);
                    }

                    lastProcessedSequence = Math.Max(lastProcessedSequence, rec.SequenceNumber);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Corrupt payload encountered in outbox record '{Id}' at sequence {Seq}.", rec.Id, rec.SequenceNumber);
                    httpContext.Response.Headers.Append("X-Resync-Required", "true");
                    // Snapshot recovery is signalled; skip the corrupt row so it cannot trap recovery forever.
                    lastProcessedSequence = rec.SequenceNumber;
                }
            }

            if (tx != null)
            {
                await tx.CommitAsync(ct);
            }

            return Results.Ok(new EventsSyncResponse(
                Events: eventEnvelopes,
                LatestSequenceNumber: lastProcessedSequence,
                HasMore: hasMore,
                SyncedAtUtc: DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in EventSync: {Message}", ex.Message);
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, detail: "Event synchronization could not be completed.");
            }
        }
    }
