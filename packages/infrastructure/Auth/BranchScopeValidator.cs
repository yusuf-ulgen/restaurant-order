using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Redis;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Authoritative validation that a branch belongs to the given tenant.
/// Employs Redis short-lived caching for performance.
/// </summary>
public sealed class BranchScopeValidator : IBranchScopeValidator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly ILogger<BranchScopeValidator> _logger;

    public BranchScopeValidator(
        RestaurantOrderDbContext dbContext,
        IRedisDatabaseProvider redisProvider,
        ILogger<BranchScopeValidator> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ValidateBranchBelongsToTenantAsync(Guid tenantId, Guid branchId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty)
        {
            return false;
        }

        var cacheKey = $"branch_scope_valid:{tenantId:D}:{branchId:D}";
        try
        {
            var db = await _redisProvider.GetDatabaseAsync(ct);
            if (db != null)
            {
                var cached = await db.StringGetAsync(cacheKey);
                if (cached.HasValue)
                {
                    return cached == "1";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Redis check failed in BranchScopeValidator: {Message}", ex.Message);
        }

        try
        {
            var exists = await _dbContext.Branches
                .AsNoTracking()
                .AnyAsync(b => b.TenantId == new TenantId(tenantId) && b.Id == new BranchId(branchId), ct);

            try
            {
                var db = await _redisProvider.GetDatabaseAsync(ct);
                if (db != null)
                {
                    await db.StringSetAsync(cacheKey, exists ? "1" : "0", CacheTtl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Redis cache write failed in BranchScopeValidator: {Message}", ex.Message);
            }

            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate branch {BranchId} for tenant {TenantId}.", branchId, tenantId);
            return false;
        }
    }
}
