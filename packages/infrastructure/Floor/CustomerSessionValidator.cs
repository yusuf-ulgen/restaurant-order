using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Redis;

namespace RestaurantOrder.Infrastructure.Floor;

/// <summary>
/// Validates whether a dining session is active (not closed) for customer JWT access tokens.
/// Employs fail-closed semantics: any closed session, missing session, or database/cache fault rejects access.
/// </summary>
public sealed class CustomerSessionValidator : ICustomerSessionValidator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CustomerSessionValidator> _logger;

    public CustomerSessionValidator(
        RestaurantOrderDbContext dbContext,
        IRedisDatabaseProvider redisProvider,
        IHostEnvironment environment,
        ILogger<CustomerSessionValidator> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ValidateSessionActiveAsync(Guid tableSessionId, CancellationToken ct = default)
    {
        if (tableSessionId == Guid.Empty)
        {
            return false;
        }

        var cacheKey = $"customer_session:{_environment.EnvironmentName}:{tableSessionId:D}";

        // 1. Try fast Redis cache
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
            _logger.LogWarning("Redis cache check failed in CustomerSessionValidator: {Message}", ex.Message);
        }

        // 2. Query PostgreSQL database source-of-truth
        // IgnoreQueryFilters is required because tenant context is not yet resolved during OnTokenValidated
        var session = await _dbContext.DiningSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.Id == DiningSessionId.From(tableSessionId))
            .Select(s => new { s.Status })
            .FirstOrDefaultAsync(ct);

        if (session == null)
        {
            return false;
        }

        var isActive = session.Status != DiningSessionStatus.Closed;

        // Cache result with short TTL
        try
        {
            var db = await _redisProvider.GetDatabaseAsync(ct);
            if (db != null)
            {
                await db.StringSetAsync(cacheKey, isActive ? "1" : "0", CacheTtl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to set Redis cache in CustomerSessionValidator: {Message}", ex.Message);
        }

        return isActive;
    }
}
