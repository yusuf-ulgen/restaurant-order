using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.RestaurantConfig;

public partial class RestaurantConfigService : IRestaurantConfigService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<RestaurantConfigService> _logger;

    public RestaurantConfigService(
        RestaurantOrderDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<RestaurantConfigService> logger)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    private void AddAuditEvent(
        TenantId tenantId,
        string eventType,
        AuthenticatedPrincipal actor,
        BranchId? branchId = null,
        object? details = null)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext?.Request.Headers.UserAgent.ToString();
        var detailsJson = details != null ? JsonSerializer.Serialize(details) : null;

        var auditEvent = SecurityAuditEvent.Create(
            tenantId: tenantId,
            eventType: eventType,
            nowUtc: DateTimeOffset.UtcNow,
            userId: new UserId(actor.SubjectId),
            branchId: branchId,
            ipAddress: ipAddress,
            userAgent: userAgent,
            detailsJson: detailsJson);

        _dbContext.SecurityAuditEvents.Add(auditEvent);
    }

    private void EnsureTenantAccess(TenantId tenantId, AuthenticatedPrincipal actor)
    {
        if (actor.Scope.TenantId != tenantId.Value)
        {
            throw new InvalidAuthorizationScopeException("Actor is not authorized for this tenant.");
        }
    }

    private void EnsureBranchAccess(TenantId tenantId, BranchId branchId, AuthenticatedPrincipal actor)
    {
        EnsureTenantAccess(tenantId, actor);

        if (actor.Role == AuthRole.BranchManager)
        {
            if (!actor.Scope.BranchId.HasValue || actor.Scope.BranchId.Value != branchId.Value)
            {
                throw new InvalidAuthorizationScopeException("BranchManager is only authorized to access their assigned branch.");
            }
        }
    }

    private async Task<Branch> GetBranchWithAccessCheckAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        return await _dbContext.Branches
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == branchId, ct)
            ?? throw new ResourceNotFoundException($"Branch '{branchId.Value}' was not found.");
    }

    private static void VerifyConcurrencyToken(Guid expectedToken, Guid? providedToken)
    {
        if (!providedToken.HasValue || providedToken.Value == Guid.Empty)
        {
            throw new ConcurrencyPreconditionException("Concurrency token is required.");
        }

        if (providedToken.Value != expectedToken)
        {
            throw new ConcurrencyConflictException(
                $"Stale concurrency token. Provided '{providedToken.Value}', but current token is '{expectedToken}'.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private static BrandDto MapBrand(Brand brand) => new(
        brand.Id.Value,
        brand.TenantId.Value,
        brand.Name,
        brand.Slug.Value,
        brand.Status.ToString(),
        brand.CreatedAtUtc,
        brand.UpdatedAtUtc,
        brand.ConcurrencyToken);

    private static BranchDto MapBranch(Branch branch) => new(
        branch.Id.Value,
        branch.TenantId.Value,
        branch.BrandId.Value,
        branch.Name,
        branch.Slug.Value,
        branch.Timezone.Id,
        branch.Currency.Code,
        branch.Status.ToString(),
        branch.CreatedAtUtc,
        branch.UpdatedAtUtc,
        branch.ConcurrencyToken);
}
