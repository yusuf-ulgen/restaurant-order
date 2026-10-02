using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// PostgreSQL persistent store for platform SuperAdmin sessions and token families.
/// Completely decoupled from tenant RLS tables; provides restart-safe and multi-instance safe
/// session persistence with atomic concurrency, reuse detection, and least privilege via SECURITY DEFINER functions.
/// </summary>
public sealed class PostgreSqlPlatformSessionStore : IPlatformSessionStore
{
    private readonly RestaurantOrderDbContext _dbContext;

    public PostgreSqlPlatformSessionStore(RestaurantOrderDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task CreateSessionAsync(
        UserId userId,
        Guid sessionId,
        Guid familyId,
        string tokenHash,
        TimeSpan sessionLifetime,
        DateTimeOffset nowUtc)
    {
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT iam.create_platform_session(@sessionId, @userId, @familyId, @tokenId, @tokenHash, @nowUtc, @sessionExpires, @tokenExpires, NULL, NULL);";

        var p0 = cmd.CreateParameter(); p0.ParameterName = "sessionId"; p0.Value = sessionId; cmd.Parameters.Add(p0);
        var p1 = cmd.CreateParameter(); p1.ParameterName = "userId"; p1.Value = userId.Value; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "familyId"; p2.Value = familyId; cmd.Parameters.Add(p2);
        var p3 = cmd.CreateParameter(); p3.ParameterName = "tokenId"; p3.Value = Guid.CreateVersion7(); cmd.Parameters.Add(p3);
        var p4 = cmd.CreateParameter(); p4.ParameterName = "tokenHash"; p4.Value = tokenHash; cmd.Parameters.Add(p4);
        var p5 = cmd.CreateParameter(); p5.ParameterName = "nowUtc"; p5.Value = nowUtc; cmd.Parameters.Add(p5);
        var p6 = cmd.CreateParameter(); p6.ParameterName = "sessionExpires"; p6.Value = nowUtc.Add(sessionLifetime); cmd.Parameters.Add(p6);
        var p7 = cmd.CreateParameter(); p7.ParameterName = "tokenExpires"; p7.Value = nowUtc.Add(sessionLifetime); cmd.Parameters.Add(p7);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(bool Success, bool ReuseDetected, PlatformSessionInfo? Session)> RotateTokenAsync(
        string oldTokenHash,
        string newTokenHash,
        TimeSpan tokenLifetime,
        DateTimeOffset nowUtc)
    {
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        var newTokenId = Guid.CreateVersion7();
        var newExpiresAt = nowUtc.Add(tokenLifetime);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT success, is_reuse, out_session_id, out_user_id, out_family_id
            FROM iam.rotate_platform_refresh_token(@oldTokenHash, @newTokenId, @newTokenHash, @nowUtc, @newExpiresAt);";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "oldTokenHash"; p1.Value = oldTokenHash; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "newTokenId"; p2.Value = newTokenId; cmd.Parameters.Add(p2);
        var p3 = cmd.CreateParameter(); p3.ParameterName = "newTokenHash"; p3.Value = newTokenHash; cmd.Parameters.Add(p3);
        var p4 = cmd.CreateParameter(); p4.ParameterName = "nowUtc"; p4.Value = nowUtc; cmd.Parameters.Add(p4);
        var p5 = cmd.CreateParameter(); p5.ParameterName = "newExpiresAt"; p5.Value = newExpiresAt; cmd.Parameters.Add(p5);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return (false, false, null);
        }

        var success = reader.GetBoolean(0);
        var isReuse = reader.GetBoolean(1);
        var sessionId = reader.IsDBNull(2) ? Guid.Empty : reader.GetGuid(2);
        var userId = reader.IsDBNull(3) ? Guid.Empty : reader.GetGuid(3);
        var familyId = reader.IsDBNull(4) ? Guid.Empty : reader.GetGuid(4);

        if (success)
        {
            var updatedSession = new PlatformSessionInfo(
                SessionId: sessionId,
                UserId: UserId.From(userId),
                FamilyId: familyId,
                CurrentTokenHash: newTokenHash,
                CreatedAtUtc: nowUtc,
                LastSeenAtUtc: nowUtc,
                ExpiresAtUtc: newExpiresAt,
                IsRevoked: false);
            return (true, false, updatedSession);
        }

        if (isReuse)
        {
            var revokedSession = sessionId != Guid.Empty
                ? new PlatformSessionInfo(sessionId, UserId.From(userId), familyId, string.Empty, nowUtc, nowUtc, nowUtc, true)
                : null;
            return (false, true, revokedSession);
        }

        return (false, false, null);
    }

    public async Task<bool> RevokeSessionAsync(Guid sessionId, string reason, DateTimeOffset nowUtc, UserId? userId = null)
    {
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT iam.revoke_platform_session(@userId, @sessionId, @reason, @nowUtc);";

        var p0 = cmd.CreateParameter(); p0.ParameterName = "userId"; p0.Value = (object?)userId?.Value ?? DBNull.Value; cmd.Parameters.Add(p0);
        var p1 = cmd.CreateParameter(); p1.ParameterName = "sessionId"; p1.Value = sessionId; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "reason"; p2.Value = reason; cmd.Parameters.Add(p2);
        var p3 = cmd.CreateParameter(); p3.ParameterName = "nowUtc"; p3.Value = nowUtc; cmd.Parameters.Add(p3);

        var result = await cmd.ExecuteScalarAsync();
        return result is bool b && b;
    }

    public async Task RevokeAllUserSessionsAsync(UserId userId, DateTimeOffset nowUtc)
    {
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT iam.revoke_all_user_platform_sessions(@userId, @nowUtc);";

        var p0 = cmd.CreateParameter(); p0.ParameterName = "userId"; p0.Value = userId.Value; cmd.Parameters.Add(p0);
        var p1 = cmd.CreateParameter(); p1.ParameterName = "nowUtc"; p1.Value = nowUtc; cmd.Parameters.Add(p1);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<PlatformSessionInfo>> GetActiveSessionsAsync(UserId userId, DateTimeOffset nowUtc)
    {
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, user_id, family_id, current_token_hash, created_at_utc, last_seen_at_utc, expires_at_utc, is_revoked
            FROM iam.list_active_platform_sessions(@userId, @nowUtc);";

        var p0 = cmd.CreateParameter(); p0.ParameterName = "userId"; p0.Value = userId.Value; cmd.Parameters.Add(p0);
        var p1 = cmd.CreateParameter(); p1.ParameterName = "nowUtc"; p1.Value = nowUtc; cmd.Parameters.Add(p1);

        var list = new List<PlatformSessionInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PlatformSessionInfo(
                SessionId: reader.GetGuid(0),
                UserId: UserId.From(reader.GetGuid(1)),
                FamilyId: reader.GetGuid(2),
                CurrentTokenHash: reader.GetString(3),
                CreatedAtUtc: reader.GetFieldValue<DateTimeOffset>(4),
                LastSeenAtUtc: reader.GetFieldValue<DateTimeOffset>(5),
                ExpiresAtUtc: reader.GetFieldValue<DateTimeOffset>(6),
                IsRevoked: reader.GetBoolean(7)));
        }

        return list;
    }

    public async Task<bool> IsSessionActiveAsync(Guid sessionId, DateTimeOffset nowUtc)
    {
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT iam.is_platform_session_active(@sessionId, @nowUtc);";

        var p0 = cmd.CreateParameter(); p0.ParameterName = "sessionId"; p0.Value = sessionId; cmd.Parameters.Add(p0);
        var p1 = cmd.CreateParameter(); p1.ParameterName = "nowUtc"; p1.Value = nowUtc; cmd.Parameters.Add(p1);

        var result = await cmd.ExecuteScalarAsync();
        return result is bool b && b;
    }
}
