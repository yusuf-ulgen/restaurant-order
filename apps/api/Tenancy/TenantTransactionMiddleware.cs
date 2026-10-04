using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Api.Tenancy;

/// <summary>
/// Enforces an explicit database transaction boundary for authenticated tenant requests.
/// Sets the PostgreSQL session setting 'app.current_tenant_id' within the transaction
/// to enable Row-Level Security (RLS) policies.
/// Commits on successful status codes (2xx/3xx) and rolls back on failures or unhandled exceptions.
/// Unauthenticated and platform-scoped requests bypass the transaction boundary.
/// </summary>
public class TenantTransactionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantTransactionMiddleware> _logger;

    public TenantTransactionMiddleware(
        RequestDelegate next,
        ILogger<TenantTransactionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextAccessor accessor,
        RestaurantOrderDbContext dbContext)
    {
        var tenantContext = accessor.TenantContext;

        if (context.Request.Path.StartsWithSegments("/api/v1/test") ||
            context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var tenantId = tenantContext.TenantId;
        if (!tenantId.HasValue && context.User?.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = context.User.FindFirst(RestaurantOrder.Application.Auth.JwtClaimNames.TenantId)?.Value
                ?? context.User.FindFirst("tenant_id")?.Value
                ?? context.User.FindFirst("tenantid")?.Value;
            if (Guid.TryParse(tenantClaim, out var claimTid))
            {
                tenantId = claimTid;
                if (!tenantContext.HasTenant)
                {
                    accessor.TenantContext = new TenantContext(claimTid, isAuthenticated: true);
                }
            }
        }

        if (tenantId.HasValue && tenantId.Value != Guid.Empty && (tenantContext.IsAuthenticated || context.User?.Identity?.IsAuthenticated == true))
        {
            var activeTenantId = tenantId.Value;
            _logger.LogDebug("[TENANT TX] Starting tenant transaction for tenant '{TenantId}' on '{Path}'.",
                activeTenantId, context.Request.Path);

            await using var tx = await dbContext.BeginTenantTransactionAsync(activeTenantId, cancellationToken: context.RequestAborted);

            try
            {
                await _next(context);

                if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 400)
                {
                    await tx.CommitAsync(context.RequestAborted);
                    _logger.LogDebug("[TENANT TX] Committed transaction for tenant '{TenantId}'. Status: {StatusCode}",
                        tenantId, context.Response.StatusCode);
                }
                else
                {
                    await tx.RollbackAsync(context.RequestAborted);
                    _logger.LogDebug("[TENANT TX] Rolled back transaction for tenant '{TenantId}'. Status: {StatusCode}",
                        tenantId, context.Response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TENANT TX] Exception during request execution. Rolling back transaction for tenant '{TenantId}'.", tenantId);
                await tx.RollbackAsync(context.RequestAborted);
                throw;
            }
            finally
            {
                await tx.DisposeAsync();
                await dbContext.ClearTenantSessionAsync(context.RequestAborted);
            }
        }
        else
        {
            await _next(context);
        }
    }
}
