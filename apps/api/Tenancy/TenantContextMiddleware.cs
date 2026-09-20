using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RestaurantOrder.Application.Tenancy;

namespace RestaurantOrder.Api.Tenancy;

/// <summary>
/// Middleware responsible for extracting correlation IDs, resolving tenant context,
/// enforcing tenant presence on endpoints decorated with <see cref="RequireTenantAttribute"/>,
/// and guaranteeing ambient context cleanup on request completion.
/// </summary>
public class TenantContextMiddleware
{
    public const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantContextMiddleware> _logger;

    public TenantContextMiddleware(
        RequestDelegate next,
        ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextResolver resolver,
        ITenantContextAccessor accessor)
    {
        // 1. Correlation ID management
        var correlationId = context.Request.Headers[CorrelationIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("D");
        }

        context.Response.Headers[CorrelationIdHeader] = correlationId;

        // 2. Resolve tenant context via registered strategy
        var tenantContext = await resolver.ResolveAsync(context.RequestAborted);
        accessor.TenantContext = tenantContext;

        try
        {
            // 3. Enforce endpoint metadata requirement
            var endpoint = context.GetEndpoint();
            var requireTenant = endpoint?.Metadata.GetMetadata<RequireTenantAttribute>() != null;

            if (requireTenant && (!tenantContext.HasTenant || !tenantContext.IsAuthenticated))
            {
                _logger.LogWarning(
                    "[TENANT SECURITY] Blocked unauthenticated access to tenant-scoped endpoint '{Path}'. CorrelationId: '{CorrelationId}'.",
                    context.Request.Path, correlationId);

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/problem+json";

                var problemDetails = new ProblemDetails
                {
                    Type = "https://restaurant-order.io/errors/tenant-context-required",
                    Title = "Tenant Context Required",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "A valid, authenticated tenant context is required to access this endpoint.",
                    Instance = context.Request.Path,
                    Extensions =
                    {
                        ["correlationId"] = correlationId
                    }
                };

                await context.Response.WriteAsJsonAsync(
                    problemDetails,
                    options: (JsonSerializerOptions?)null,
                    contentType: "application/problem+json",
                    cancellationToken: context.RequestAborted);
                return;
            }

            if (tenantContext.HasTenant)
            {
                _logger.LogDebug(
                    "[TENANT ACCESS] Executing '{Path}' with Tenant '{TenantId}', Branch '{BranchId}'. CorrelationId: '{CorrelationId}'.",
                    context.Request.Path, tenantContext.TenantId, tenantContext.BranchId, correlationId);
            }

            await _next(context);
        }
        finally
        {
            // 4. Guaranteed context cleanup to prevent connection pool / thread leakage
            accessor.TenantContext = TenantContext.Empty;
        }
    }
}
