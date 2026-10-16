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
                await using var tx = await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken: ct);

                var sessionWithTable = await (
                    from s in dbContext.DiningSessions
                    join t in dbContext.RestaurantTables on s.TableId equals t.Id
                    where s.TenantId == new TenantId(tenantId) &&
                          s.BranchId == new BranchId(branchId) &&
                          s.Id == DiningSessionId.From(sessionId) &&
                          t.TenantId == new TenantId(tenantId) &&
                          t.BranchId == new BranchId(branchId)
                    select new
                    {
                        SessionId = s.Id.Value,
                        TenantId = s.TenantId.Value,
                        BranchId = s.BranchId.Value,
                        TableId = t.Id.Value,
                        TableNumber = t.TableNumber,
                        TableName = t.Name,
                        Status = s.Status,
                        ConcurrencyToken = s.ConcurrencyToken,
                        GuestCount = s.GuestCount,
                        OpenedAtUtc = s.OpenedAtUtc,
                        MergedIntoSessionId = s.MergedIntoSessionId
                    })
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (sessionWithTable == null)
                {
                    await sessionValidator.InvalidateSessionCacheAsync(tenantId, branchId, sessionId, ct);
                    return ProblemResult(context, StatusCodes.Status401Unauthorized, "Session Expired", "The dining session is closed, merged, or does not exist.");
                }

                if (sessionWithTable.Status == DiningSessionStatus.Closed || sessionWithTable.MergedIntoSessionId.HasValue)
                {
                    await sessionValidator.InvalidateSessionCacheAsync(tenantId, branchId, sessionId, ct);
                    return ProblemResult(context, StatusCodes.Status401Unauthorized, "Session Expired", "The dining session has been closed or merged.");
                }

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{sessionWithTable.ConcurrencyToken:D}\"";

                var response = new CustomerSessionDto(
                    SessionId: sessionWithTable.SessionId,
                    TenantId: sessionWithTable.TenantId,
                    BranchId: sessionWithTable.BranchId,
                    TableId: sessionWithTable.TableId,
                    TableNumber: sessionWithTable.TableNumber,
                    TableName: sessionWithTable.TableName,
                    SessionStatus: sessionWithTable.Status.ToString(),
                    ConcurrencyToken: sessionWithTable.ConcurrencyToken,
                    GuestCount: sessionWithTable.GuestCount,
                    OpenedAtUtc: sessionWithTable.OpenedAtUtc);

                return Results.Ok(response);
            }
            catch (JwtClaimValidationException jex)
            {
                return ProblemResult(context, StatusCodes.Status401Unauthorized, "Unauthorized", $"Invalid session claims: {jex.Message}");
            }
            catch (Exception)
            {
                return ProblemResult(context, StatusCodes.Status500InternalServerError, "Internal Server Error", "An error occurred while verifying the customer session.");
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
