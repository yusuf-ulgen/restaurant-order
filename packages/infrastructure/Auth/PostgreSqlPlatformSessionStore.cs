using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// PostgreSQL persistent store for platform SuperAdmin sessions and token families.
/// Completely decoupled from tenant RLS tables; provides restart-safe and multi-instance safe
/// session persistence with atomic concurrency and reuse detection.
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
        var session = PlatformSession.Create(
            userId,
            familyId,
            tokenHash,
            sessionLifetime,
            nowUtc,
            id: sessionId);

        var token = PlatformRefreshToken.Create(
            sessionId,
            familyId,
            tokenHash,
            sessionLifetime,
            nowUtc);

        _dbContext.PlatformSessions.Add(session);
        _dbContext.PlatformRefreshTokens.Add(token);
        await _dbContext.SaveChangesAsync();
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

        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        await using var localTx = hasAmbientTx ? null : await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var dbTx = _dbContext.Database.CurrentTransaction!.GetDbTransaction();

        try
        {
            // 1. Lock the refresh token row for update to guarantee strict concurrency serialization
            await using var tokenCmd = conn.CreateCommand();
            tokenCmd.Transaction = dbTx;
            tokenCmd.CommandText = @"
                SELECT id, session_id, token_family_id, token_hash, is_revoked, replaced_by_token_id, expires_at_utc
                FROM iam.platform_refresh_tokens
                WHERE token_hash = @tokenHash
                FOR UPDATE;";
            var hashParam = tokenCmd.CreateParameter();
            hashParam.ParameterName = "tokenHash";
            hashParam.Value = oldTokenHash;
            tokenCmd.Parameters.Add(hashParam);

            Guid tokenId;
            Guid sessionId;
            Guid familyId;
            bool isRevoked;
            Guid? replacedByTokenId = null;
            DateTimeOffset expiresAtUtc;

            await using (var reader = await tokenCmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                {
                    return (false, false, null);
                }

                tokenId = reader.GetGuid(0);
                sessionId = reader.GetGuid(1);
                familyId = reader.GetGuid(2);
                isRevoked = reader.GetBoolean(4);
                if (!reader.IsDBNull(5)) replacedByTokenId = reader.GetGuid(5);
                expiresAtUtc = reader.GetDateTime(6);
            }

            // 2. Fetch session details
            await using var sessionCmd = conn.CreateCommand();
            sessionCmd.Transaction = dbTx;
            sessionCmd.CommandText = @"
                SELECT id, user_id, family_id, current_token_hash, created_at_utc, last_seen_at_utc, expires_at_utc, is_revoked
                FROM iam.platform_sessions
                WHERE id = @sessionId
                FOR UPDATE;";
            var sIdParam = sessionCmd.CreateParameter();
            sIdParam.ParameterName = "sessionId";
            sIdParam.Value = sessionId;
            sessionCmd.Parameters.Add(sIdParam);

            PlatformSessionInfo? sessionInfo = null;
            await using (var sReader = await sessionCmd.ExecuteReaderAsync())
            {
                if (await sReader.ReadAsync())
                {
                    sessionInfo = new PlatformSessionInfo(
                        SessionId: sReader.GetGuid(0),
                        UserId: UserId.From(sReader.GetGuid(1)),
                        FamilyId: sReader.GetGuid(2),
                        CurrentTokenHash: sReader.GetString(3),
                        CreatedAtUtc: sReader.GetDateTime(4),
                        LastSeenAtUtc: sReader.GetDateTime(5),
                        ExpiresAtUtc: sReader.GetDateTime(6),
                        IsRevoked: sReader.GetBoolean(7));
                }
            }

            if (sessionInfo == null)
            {
                return (false, false, null);
            }

            // 3. Reuse detection: If token was already revoked or replaced, revoke family and session
            if (isRevoked || replacedByTokenId.HasValue)
            {
                await using var revokeFamilyCmd = conn.CreateCommand();
                revokeFamilyCmd.Transaction = dbTx;
                revokeFamilyCmd.CommandText = @"
                    UPDATE iam.platform_refresh_tokens
                    SET is_revoked = true, revoked_at_utc = @now
                    WHERE token_family_id = @familyId;

                    UPDATE iam.platform_sessions
                    SET is_revoked = true, revoked_reason = 'token_reuse_detected', revoked_at_utc = @now
                    WHERE id = @sessionId;";

                var nowParam = revokeFamilyCmd.CreateParameter();
                nowParam.ParameterName = "now";
                nowParam.Value = nowUtc;
                revokeFamilyCmd.Parameters.Add(nowParam);

                var famParam = revokeFamilyCmd.CreateParameter();
                famParam.ParameterName = "familyId";
                famParam.Value = familyId;
                revokeFamilyCmd.Parameters.Add(famParam);

                var sessParam = revokeFamilyCmd.CreateParameter();
                sessParam.ParameterName = "sessionId";
                sessParam.Value = sessionId;
                revokeFamilyCmd.Parameters.Add(sessParam);

                await revokeFamilyCmd.ExecuteNonQueryAsync();

                if (localTx != null) await localTx.CommitAsync();
                return (false, true, sessionInfo);
            }

            if (expiresAtUtc <= nowUtc || sessionInfo.IsRevoked || sessionInfo.ExpiresAtUtc <= nowUtc)
            {
                return (false, false, null);
            }

            // 4. Consume old token and insert new token
            var newTokenId = Guid.CreateVersion7();
            var newExpiresAt = nowUtc.Add(tokenLifetime);

            await using var rotateCmd = conn.CreateCommand();
            rotateCmd.Transaction = dbTx;
            rotateCmd.CommandText = @"
                UPDATE iam.platform_refresh_tokens
                SET is_revoked = true, replaced_by_token_id = @newTokenId, revoked_at_utc = @now
                WHERE id = @oldTokenId;

                INSERT INTO iam.platform_refresh_tokens (id, session_id, token_family_id, token_hash, is_revoked, created_at_utc, expires_at_utc)
                VALUES (@newTokenId, @sessionId, @familyId, @newTokenHash, false, @now, @newExpiresAt);

                UPDATE iam.platform_sessions
                SET current_token_hash = @newTokenHash, last_seen_at_utc = @now
                WHERE id = @sessionId;";

            var p1 = rotateCmd.CreateParameter(); p1.ParameterName = "newTokenId"; p1.Value = newTokenId; rotateCmd.Parameters.Add(p1);
            var p2 = rotateCmd.CreateParameter(); p2.ParameterName = "now"; p2.Value = nowUtc; rotateCmd.Parameters.Add(p2);
            var p3 = rotateCmd.CreateParameter(); p3.ParameterName = "oldTokenId"; p3.Value = tokenId; rotateCmd.Parameters.Add(p3);
            var p4 = rotateCmd.CreateParameter(); p4.ParameterName = "sessionId"; p4.Value = sessionId; rotateCmd.Parameters.Add(p4);
            var p5 = rotateCmd.CreateParameter(); p5.ParameterName = "familyId"; p5.Value = familyId; rotateCmd.Parameters.Add(p5);
            var p6 = rotateCmd.CreateParameter(); p6.ParameterName = "newTokenHash"; p6.Value = newTokenHash; rotateCmd.Parameters.Add(p6);
            var p7 = rotateCmd.CreateParameter(); p7.ParameterName = "newExpiresAt"; p7.Value = newExpiresAt; rotateCmd.Parameters.Add(p7);

            await rotateCmd.ExecuteNonQueryAsync();

            if (localTx != null) await localTx.CommitAsync();

            var updatedSession = sessionInfo with
            {
                CurrentTokenHash = newTokenHash,
                LastSeenAtUtc = nowUtc
            };

            return (true, false, updatedSession);
        }
        catch
        {
            if (localTx != null) await localTx.RollbackAsync();
            throw;
        }
    }

    public async Task RevokeSessionAsync(Guid sessionId, string reason, DateTimeOffset nowUtc)
    {
        var session = await _dbContext.PlatformSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session != null)
        {
            session.Revoke(reason, nowUtc);
            var tokens = await _dbContext.PlatformRefreshTokens
                .Where(rt => rt.SessionId == sessionId && !rt.IsRevoked)
                .ToListAsync();

            foreach (var t in tokens)
            {
                t.Revoke(nowUtc);
            }

            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task RevokeAllUserSessionsAsync(UserId userId, DateTimeOffset nowUtc)
    {
        var sessions = await _dbContext.PlatformSessions
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .ToListAsync();

        var sessionIds = sessions.Select(s => s.Id).ToList();
        foreach (var s in sessions)
        {
            s.Revoke("revoke_all", nowUtc);
        }

        var tokens = await _dbContext.PlatformRefreshTokens
            .Where(rt => sessionIds.Contains(rt.SessionId) && !rt.IsRevoked)
            .ToListAsync();

        foreach (var t in tokens)
        {
            t.Revoke(nowUtc);
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<PlatformSessionInfo>> GetActiveSessionsAsync(UserId userId, DateTimeOffset nowUtc)
    {
        return await _dbContext.PlatformSessions
            .Where(s => s.UserId == userId && !s.IsRevoked && s.ExpiresAtUtc > nowUtc)
            .OrderByDescending(s => s.LastSeenAtUtc)
            .Select(s => new PlatformSessionInfo(
                s.Id,
                s.UserId,
                s.FamilyId,
                s.CurrentTokenHash,
                s.CreatedAtUtc,
                s.LastSeenAtUtc,
                s.ExpiresAtUtc,
                s.IsRevoked))
            .ToListAsync();
    }

    public async Task<bool> IsSessionActiveAsync(Guid sessionId, DateTimeOffset nowUtc)
    {
        return await _dbContext.PlatformSessions
            .AnyAsync(s => s.Id == sessionId && !s.IsRevoked && s.ExpiresAtUtc > nowUtc);
    }
}
