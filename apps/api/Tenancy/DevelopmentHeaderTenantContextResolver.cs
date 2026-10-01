using RestaurantOrder.Application.Tenancy;

namespace RestaurantOrder.Api.Tenancy;

/// <summary>
/// Resolves tenant and branch context from incoming HTTP headers ('X-Tenant-Id', 'X-Branch-Id').
/// STRICT SECURITY BOUNDARY:
/// 1. Only permitted when environment is strictly Development.
/// 2. Requires explicit opt-in configuration ('Tenancy:AllowDevHeaderOverride = true').
/// 3. Throws immediately if invoked in Production or Staging to prevent silent activation.
/// </summary>
public class DevelopmentHeaderTenantContextResolver : ITenantContextResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DevelopmentHeaderTenantContextResolver> _logger;

    public const string TenantHeaderName = "X-Tenant-Id";
    public const string BranchHeaderName = "X-Branch-Id";

    public DevelopmentHeaderTenantContextResolver(
        IHttpContextAccessor httpContextAccessor,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<DevelopmentHeaderTenantContextResolver> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<ITenantContext> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"FATAL SECURITY VIOLATION: '{nameof(DevelopmentHeaderTenantContextResolver)}' is strictly prohibited in '{_environment.EnvironmentName}' environment.");
        }

        var allowDevHeaderOverride = _configuration.GetValue<bool>("Tenancy:AllowDevHeaderOverride", false);
        if (!allowDevHeaderOverride)
        {
            _logger.LogDebug("Development header tenant resolution is disabled (Tenancy:AllowDevHeaderOverride is false).");
            return Task.FromResult(TenantContext.Empty);
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Task.FromResult(TenantContext.Empty);
        }

        if (!httpContext.Request.Headers.TryGetValue(TenantHeaderName, out var tenantHeaderValues) ||
            string.IsNullOrWhiteSpace(tenantHeaderValues))
        {
            return Task.FromResult(TenantContext.Empty);
        }

        var rawTenantId = tenantHeaderValues.ToString().Trim();
        if (!Guid.TryParse(rawTenantId, out var tenantId) || tenantId == Guid.Empty)
        {
            _logger.LogWarning("Malformed or empty '{TenantHeader}' header received: '{RawTenantId}'. Failing closed.",
                TenantHeaderName, rawTenantId);
            return Task.FromResult(TenantContext.Empty);
        }

        Guid? branchId = null;
        if (httpContext.Request.Headers.TryGetValue(BranchHeaderName, out var branchHeaderValues) &&
            !string.IsNullOrWhiteSpace(branchHeaderValues))
        {
            var rawBranchId = branchHeaderValues.ToString().Trim();
            if (Guid.TryParse(rawBranchId, out var parsedBranchId) && parsedBranchId != Guid.Empty)
            {
                branchId = parsedBranchId;
            }
        }

        _logger.LogDebug("[DEV-TENANCY] Resolved tenant '{TenantId}' (Branch: '{BranchId}') from request headers.",
            tenantId, branchId);

        ITenantContext context = new TenantContext(tenantId, branchId, isAuthenticated: true);
        return Task.FromResult(context);
    }
}
