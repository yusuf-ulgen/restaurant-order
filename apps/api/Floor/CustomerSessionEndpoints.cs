using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Api.Floor;

/// <summary>
/// Endpoints for customer dining session state recovery.
/// Allows authenticated customers (via HttpOnly session cookie) to securely restore active session metadata.
/// </summary>
public static class CustomerSessionEndpoints
{
    public static IEndpointRouteBuilder MapCustomerSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customer")
            .WithTags("Customer Session");

        group.MapGet("/session", async (
            HttpContext context,
            IJwtClaimPrincipalParser parser,
            RestaurantOrderDbContext dbContext,
            ICustomerSessionValidator sessionValidator,
            CancellationToken ct) =>
        {
            try
            {
                // 1. Derive identity and claims strictly from verified authentication context.
                // Request body and query parameters are never accepted for session identification.
                var actor = FloorEndpointHelpers.GetActor(context, parser);

                if (actor.Role != AuthRole.Customer)
                {
                    return ProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden", "Only customers may access the dining session state.");
                }

                if (actor.Scope.TenantId == null ||
                    actor.Scope.BranchId == null ||
                    !actor.Scope.TableSessionId.HasValue)
                {
                    return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Customer token is missing required session claims.");
                }

                var tenantId = actor.Scope.TenantId.Value;
                var branchId = actor.Scope.BranchId.Value;
                var sessionId = actor.Scope.TableSessionId.Value;

                // 2. Query PostgreSQL source-of-truth inside tenant transaction.
                // Enforces PostgreSQL forced Row-Level Security (RLS) under runtime roles.
                var hasAmbientTx = dbContext.Database.CurrentTransaction != null;
                Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? localTx = null;
                if (!hasAmbientTx)
                {
                    localTx = await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken: ct);
                }

                try
                {
                    var session = await dbContext.DiningSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.TenantId == new TenantId(tenantId) &&
                                                  s.BranchId == new BranchId(branchId) &&
                                                  s.Id == DiningSessionId.From(sessionId), ct);

                    if (session == null)
                    {
                        await sessionValidator.InvalidateSessionCacheAsync(tenantId, branchId, sessionId, ct);
                        return ProblemResult(context, StatusCodes.Status401Unauthorized, "Session Expired", "The dining session is closed, merged, or does not exist.");
                    }

                    if (session.Status == DiningSessionStatus.Closed || session.MergedIntoSessionId.HasValue)
                    {
                        await sessionValidator.InvalidateSessionCacheAsync(tenantId, branchId, sessionId, ct);
                        return ProblemResult(context, StatusCodes.Status401Unauthorized, "Session Expired", "The dining session has been closed or merged.");
                    }

                    var table = await dbContext.RestaurantTables
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.TenantId == new TenantId(tenantId) &&
                                                  t.BranchId == new BranchId(branchId) &&
                                                  t.Id == session.TableId, ct);

                    if (table == null)
                    {
                        await sessionValidator.InvalidateSessionCacheAsync(tenantId, branchId, sessionId, ct);
                        return ProblemResult(context, StatusCodes.Status401Unauthorized, "Session Expired", "The assigned table was not found.");
                    }

                    if (localTx != null)
                    {
                        await localTx.CommitAsync(ct);
                    }

                    context.Response.Headers.ETag = $"\"{session.ConcurrencyToken:D}\"";

                    var response = new CustomerSessionDto(
                        SessionId: session.Id.Value,
                        TenantId: session.TenantId.Value,
                        BranchId: session.BranchId.Value,
                        TableId: table.Id.Value,
                        TableNumber: table.TableNumber,
                        TableName: table.Name,
                        SessionStatus: session.Status.ToString(),
                        ConcurrencyToken: session.ConcurrencyToken,
                        GuestCount: session.GuestCount,
                        OpenedAtUtc: session.OpenedAtUtc);

                    return Results.Ok(response);
                }
                finally
                {
                    if (localTx != null)
                    {
                        await localTx.DisposeAsync();
                    }
                }
            }
            catch (JwtClaimValidationException jex)
            {
                return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", $"Invalid session claims: {jex.Message}");
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("RestaurantOrder.Api.Floor.CustomerSession");
                logger?.LogError(ex, "Unexpected error retrieving customer session: {Message}", ex.Message);
                return FloorEndpointHelpers.HandleException(ex, context);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "Customer" })
        .WithName("GetCurrentCustomerSession")
        .WithSummary("Retrieve active dining session metadata for the currently authenticated customer");

        return app;
    }

    private static IResult ProblemResult(HttpContext context, int status, string title, string detail)
    {
        return Results.Json(new
        {
            type = $"https://httpstatuses.com/{status}",
            title,
            status,
            detail,
            instance = context.Request.Path.Value
        }, statusCode: status, contentType: "application/problem+json");
    }
}
