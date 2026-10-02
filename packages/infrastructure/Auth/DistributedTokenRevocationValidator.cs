using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Redis;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Validates access token active state against PostgreSQL source-of-truth with short-TTL Redis caching.
/// Executes before tenant context is set; immediately rejects revoked sessions or outdated security versions.
/// </summary>
public sealed class DistributedTokenRevocationValidator : ITokenRevocationValidator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DistributedTokenRevocationValidator> _logger;

    public DistributedTokenRevocationValidator(
        RestaurantOrderDbContext dbContext,
        IRedisDatabaseProvider redisProvider,
        IHostEnvironment environment,
        ILogger<DistributedTokenRevocationValidator> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ValidateTokenActiveAsync(
        Guid sessionId,
        Guid userId,
        int securityVersion,
        CancellationToken ct = default)
    {
        var secVerKey = AuthRedisKeyBuilder.UserSecurityVersion(_environment.EnvironmentName, userId);
        var sessionKey = AuthRedisKeyBuilder.SessionActiveCache(_environment.EnvironmentName, sessionId);
        var userSessionsKey = AuthRedisKeyBuilder.UserSessions(_environment.EnvironmentName, userId);

        // 1. Try fast Redis cache
        try
        {
            var db = await _redisProvider.GetDatabaseAsync(ct);
            if (db != null)
            {
                var cachedSecVer = await db.StringGetAsync(secVerKey);
                if (cachedSecVer.HasValue && int.TryParse((string?)cachedSecVer, out var secVer) && secVer != securityVersion)
                {
                    return false;
                }

                var cachedSession = await db.StringGetAsync(sessionKey);
                if (cachedSession == "0") return false;

                if (cachedSession == "1" && cachedSecVer.HasValue && int.TryParse((string?)cachedSecVer, out var sv) && sv == securityVersion)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Redis cache check failed in token validator: {ExceptionType}", ex.GetType().Name);
        }

        // 2. Query PostgreSQL source-of-truth via SECURITY DEFINER function
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        if (_dbContext.Database.CurrentTransaction != null)
        {
            cmd.Transaction = _dbContext.Database.CurrentTransaction.GetDbTransaction();
        }

        cmd.CommandText = "SELECT is_valid, failure_reason FROM iam.validate_token_session(@sessionId, @userId, @securityVersion);";

        var pSid = cmd.CreateParameter(); pSid.ParameterName = "sessionId"; pSid.Value = sessionId; cmd.Parameters.Add(pSid);
        var pUid = cmd.CreateParameter(); pUid.ParameterName = "userId"; pUid.Value = userId; cmd.Parameters.Add(pUid);
        var pSec = cmd.CreateParameter(); pSec.ParameterName = "securityVersion"; pSec.Value = securityVersion; cmd.Parameters.Add(pSec);

        bool isValid = false;
        string? failureReason = null;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                isValid = reader.GetBoolean(0);
                failureReason = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }

        if (!isValid)
        {
            _logger.LogInformation("Token validation failed for session {SessionId}: {Reason}", sessionId, failureReason);
        }

        // 3. Cache result in Redis for fast repeated checks
        try
        {
            var db = await _redisProvider.GetDatabaseAsync(ct);
            if (db != null)
            {
                if (isValid)
                {
                    await db.StringSetAsync(sessionKey, "1", CacheTtl);
                    await db.StringSetAsync(secVerKey, securityVersion.ToString(), CacheTtl);
                    await db.SetAddAsync(userSessionsKey, sessionId.ToString("N"));
                    await db.KeyExpireAsync(userSessionsKey, TimeSpan.FromHours(1));
                }
                else
                {
                    await db.StringSetAsync(sessionKey, "0", CacheTtl);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to update token validator Redis cache: {ExceptionType}", ex.GetType().Name);
        }

        return isValid;
    }

    public async Task InvalidateSessionCacheAsync(Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            var db = await _redisProvider.GetDatabaseAsync(ct);
            if (db != null)
            {
                var sessionKey = AuthRedisKeyBuilder.SessionActiveCache(_environment.EnvironmentName, sessionId);
                await db.StringSetAsync(sessionKey, "0", CacheTtl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to invalidate session cache in Redis: {ExceptionType}", ex.GetType().Name);
        }
    }

    public async Task InvalidateUserCacheAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var db = await _redisProvider.GetDatabaseAsync(ct);
            if (db != null)
            {
                var secVerKey = AuthRedisKeyBuilder.UserSecurityVersion(_environment.EnvironmentName, userId);
                var userSessionsKey = AuthRedisKeyBuilder.UserSessions(_environment.EnvironmentName, userId);

                await db.KeyDeleteAsync(secVerKey);

                var sessionMembers = await db.SetMembersAsync(userSessionsKey);
                foreach (var member in sessionMembers)
                {
                    if (Guid.TryParse((string?)member, out var sId))
                    {
                        var sKey = AuthRedisKeyBuilder.SessionActiveCache(_environment.EnvironmentName, sId);
                        await db.StringSetAsync(sKey, "0", CacheTtl);
                    }
                }

                await db.KeyDeleteAsync(userSessionsKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to invalidate user cache in Redis: {ExceptionType}", ex.GetType().Name);
        }
    }
}
