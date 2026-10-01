using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

public sealed partial class PostgreSqlIamBootstrapGateway
{
    public async Task<RefreshTokenRotationLookupDto?> LookupRefreshTokenForRotationAsync(string tokenHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, @"
            SELECT token_id, tenant_id, session_id, token_family_id, expires_at_utc,
                   is_revoked, replaced_by_token_id, user_id, auth_method,
                   session_is_revoked, session_expires_at_utc
            FROM iam.lookup_refresh_token_for_rotation(@tokenHash);");

        var param = cmd.CreateParameter();
        param.ParameterName = "tokenHash";
        param.Value = tokenHash;
        cmd.Parameters.Add(param);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var replacedBy = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6);
            return new RefreshTokenRotationLookupDto(
                TokenId: reader.GetGuid(0),
                TenantId: reader.GetGuid(1),
                SessionId: reader.GetGuid(2),
                TokenFamilyId: reader.GetGuid(3),
                ExpiresAtUtc: reader.GetFieldValue<DateTimeOffset>(4),
                IsRevoked: reader.GetBoolean(5),
                ReplacedByTokenId: replacedBy,
                UserId: reader.GetGuid(7),
                AuthMethod: reader.GetInt32(8),
                SessionIsRevoked: reader.GetBoolean(9),
                SessionExpiresAtUtc: reader.GetFieldValue<DateTimeOffset>(10));
        }

        return null;
    }

    public async Task<bool> RotateTenantRefreshTokenAsync(
        Guid tenantId,
        Guid oldTokenId,
        Guid newTokenId,
        Guid sessionId,
        Guid familyId,
        string newTokenHash,
        TimeSpan tokenLifetime,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        await using var localTx = hasAmbientTx ? null : await _dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken: ct);

        // 1. Lock the existing token row FOR UPDATE under tenant RLS
        await using var lockCmd = CreateCommand(conn, @"
            SELECT id, is_revoked, replaced_by_token_id
            FROM iam.refresh_tokens
            WHERE id = @oldTokenId AND tenant_id = @tenantId
            FOR UPDATE;");
        var pOldId = lockCmd.CreateParameter(); pOldId.ParameterName = "oldTokenId"; pOldId.Value = oldTokenId; lockCmd.Parameters.Add(pOldId);
        var pTenant = lockCmd.CreateParameter(); pTenant.ParameterName = "tenantId"; pTenant.Value = tenantId; lockCmd.Parameters.Add(pTenant);

        bool isRevoked;
        Guid? replacedBy;
        await using (var reader = await lockCmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                return false;
            }
            isRevoked = reader.GetBoolean(1);
            replacedBy = reader.IsDBNull(2) ? null : reader.GetGuid(2);
        }

        if (isRevoked || replacedBy.HasValue)
        {
            return false;
        }

        // 2. Mark old token revoked and replaced
        await using var updateOldCmd = CreateCommand(conn, @"
            UPDATE iam.refresh_tokens
            SET is_revoked = true,
                revoked_at_utc = @nowUtc,
                replaced_by_token_id = @newTokenId
            WHERE id = @oldTokenId AND tenant_id = @tenantId;");
        var pOld2 = updateOldCmd.CreateParameter(); pOld2.ParameterName = "oldTokenId"; pOld2.Value = oldTokenId; updateOldCmd.Parameters.Add(pOld2);
        var pTen2 = updateOldCmd.CreateParameter(); pTen2.ParameterName = "tenantId"; pTen2.Value = tenantId; updateOldCmd.Parameters.Add(pTen2);
        var pNow2 = updateOldCmd.CreateParameter(); pNow2.ParameterName = "nowUtc"; pNow2.Value = nowUtc; updateOldCmd.Parameters.Add(pNow2);
        var pNew2 = updateOldCmd.CreateParameter(); pNew2.ParameterName = "newTokenId"; pNew2.Value = newTokenId; updateOldCmd.Parameters.Add(pNew2);
        await updateOldCmd.ExecuteNonQueryAsync(ct);

        // 3. Insert new token
        await using var insertCmd = CreateCommand(conn, @"
            INSERT INTO iam.refresh_tokens (
                id, tenant_id, session_id, token_family_id, token_hash, expires_at_utc, is_revoked, created_at_utc
            ) VALUES (
                @newTokenId, @tenantId, @sessionId, @familyId, @newTokenHash, @expiresAtUtc, false, @nowUtc
            );");
        var pInsId = insertCmd.CreateParameter(); pInsId.ParameterName = "newTokenId"; pInsId.Value = newTokenId; insertCmd.Parameters.Add(pInsId);
        var pInsTen = insertCmd.CreateParameter(); pInsTen.ParameterName = "tenantId"; pInsTen.Value = tenantId; insertCmd.Parameters.Add(pInsTen);
        var pInsSid = insertCmd.CreateParameter(); pInsSid.ParameterName = "sessionId"; pInsSid.Value = sessionId; insertCmd.Parameters.Add(pInsSid);
        var pInsFam = insertCmd.CreateParameter(); pInsFam.ParameterName = "familyId"; pInsFam.Value = familyId; insertCmd.Parameters.Add(pInsFam);
        var pInsHash = insertCmd.CreateParameter(); pInsHash.ParameterName = "newTokenHash"; pInsHash.Value = newTokenHash; insertCmd.Parameters.Add(pInsHash);
        var pInsExp = insertCmd.CreateParameter(); pInsExp.ParameterName = "expiresAtUtc"; pInsExp.Value = nowUtc.Add(tokenLifetime); insertCmd.Parameters.Add(pInsExp);
        var pInsNow = insertCmd.CreateParameter(); pInsNow.ParameterName = "nowUtc"; pInsNow.Value = nowUtc; insertCmd.Parameters.Add(pInsNow);
        await insertCmd.ExecuteNonQueryAsync(ct);

        // 4. Update session last seen
        await using var sessCmd = CreateCommand(conn, @"
            UPDATE iam.sessions
            SET last_seen_at_utc = @nowUtc
            WHERE id = @sessionId AND tenant_id = @tenantId;");
        var pSesId = sessCmd.CreateParameter(); pSesId.ParameterName = "sessionId"; pSesId.Value = sessionId; sessCmd.Parameters.Add(pSesId);
        var pSesTen = sessCmd.CreateParameter(); pSesTen.ParameterName = "tenantId"; pSesTen.Value = tenantId; sessCmd.Parameters.Add(pSesTen);
        var pSesNow = sessCmd.CreateParameter(); pSesNow.ParameterName = "nowUtc"; pSesNow.Value = nowUtc; sessCmd.Parameters.Add(pSesNow);
        await sessCmd.ExecuteNonQueryAsync(ct);

        if (localTx != null)
        {
            await localTx.CommitAsync(ct);
        }

        return true;
    }

    public async Task HandleTenantTokenReuseAsync(
        Guid tenantId,
        Guid familyId,
        Guid sessionId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        await using var localTx = hasAmbientTx ? null : await _dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken: ct);

        await using var cmd = CreateCommand(conn, @"
            UPDATE iam.refresh_tokens
            SET is_revoked = true, revoked_at_utc = @nowUtc
            WHERE token_family_id = @familyId AND tenant_id = @tenantId;

            UPDATE iam.sessions
            SET is_revoked = true,
                revocation_reason = 'token_reuse_detected',
                revoked_at_utc = @nowUtc
            WHERE id = @sessionId AND tenant_id = @tenantId;

            INSERT INTO iam.security_audit_events (
                id, tenant_id, event_type, created_at_utc, details_json
            ) VALUES (
                gen_random_uuid(), @tenantId, 'RefreshTokenReuseDetected', @nowUtc, @detailsJson
            );");

        var pFam = cmd.CreateParameter(); pFam.ParameterName = "familyId"; pFam.Value = familyId; cmd.Parameters.Add(pFam);
        var pTen = cmd.CreateParameter(); pTen.ParameterName = "tenantId"; pTen.Value = tenantId; cmd.Parameters.Add(pTen);
        var pSid = cmd.CreateParameter(); pSid.ParameterName = "sessionId"; pSid.Value = sessionId; cmd.Parameters.Add(pSid);
        var pNow = cmd.CreateParameter(); pNow.ParameterName = "nowUtc"; pNow.Value = nowUtc; cmd.Parameters.Add(pNow);
        var pDet = cmd.CreateParameter(); pDet.ParameterName = "detailsJson"; pDet.Value = $"{{\"familyId\":\"{familyId}\"}}"; cmd.Parameters.Add(pDet);

        await cmd.ExecuteNonQueryAsync(ct);

        if (localTx != null)
        {
            await localTx.CommitAsync(ct);
        }
    }

    public async Task IncrementSecurityVersionAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, @"
            UPDATE iam.users
            SET security_version = security_version + 1,
                updated_at_utc = @nowUtc,
                concurrency_token = gen_random_uuid()
            WHERE id = @userId;");

        var pUser = cmd.CreateParameter(); pUser.ParameterName = "userId"; pUser.Value = userId; cmd.Parameters.Add(pUser);
        var pNow = cmd.CreateParameter(); pNow.ParameterName = "nowUtc"; pNow.Value = nowUtc; cmd.Parameters.Add(pNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RevokeTenantSessionAsync(
        Guid tenantId,
        Guid sessionId,
        string reason,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        var hasAmbientTx = _dbContext.Database.CurrentTransaction != null;
        await using var localTx = hasAmbientTx ? null : await _dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken: ct);

        await using var cmd = CreateCommand(conn, @"
            UPDATE iam.sessions
            SET is_revoked = true,
                revocation_reason = @reason,
                revoked_at_utc = @nowUtc
            WHERE id = @sessionId AND tenant_id = @tenantId;

            UPDATE iam.refresh_tokens
            SET is_revoked = true,
                revoked_at_utc = @nowUtc
            WHERE session_id = @sessionId AND tenant_id = @tenantId;");

        var pSid = cmd.CreateParameter(); pSid.ParameterName = "sessionId"; pSid.Value = sessionId; cmd.Parameters.Add(pSid);
        var pTen = cmd.CreateParameter(); pTen.ParameterName = "tenantId"; pTen.Value = tenantId; cmd.Parameters.Add(pTen);
        var pRea = cmd.CreateParameter(); pRea.ParameterName = "reason"; pRea.Value = reason; cmd.Parameters.Add(pRea);
        var pNow = cmd.CreateParameter(); pNow.ParameterName = "nowUtc"; pNow.Value = nowUtc; cmd.Parameters.Add(pNow);

        await cmd.ExecuteNonQueryAsync(ct);

        if (localTx != null)
        {
            await localTx.CommitAsync(ct);
        }
    }

    public async Task RevokeAllUserSessionsAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, @"
            UPDATE iam.users
            SET security_version = security_version + 1,
                updated_at_utc = @nowUtc,
                concurrency_token = gen_random_uuid()
            WHERE id = @userId;

            UPDATE iam.sessions
            SET is_revoked = true,
                revocation_reason = 'logout_all',
                revoked_at_utc = @nowUtc
            WHERE user_id = @userId AND is_revoked = false;

            UPDATE iam.refresh_tokens rt
            SET is_revoked = true,
                revoked_at_utc = @nowUtc
            FROM iam.sessions s
            WHERE rt.session_id = s.id AND s.user_id = @userId AND rt.is_revoked = false;");

        var pUser = cmd.CreateParameter(); pUser.ParameterName = "userId"; pUser.Value = userId; cmd.Parameters.Add(pUser);
        var pNow = cmd.CreateParameter(); pNow.ParameterName = "nowUtc"; pNow.Value = nowUtc; cmd.Parameters.Add(pNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<UserSessionSummaryDto>> LookupActiveUserSessionsAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, @"
            SELECT id, tenant_id, auth_method, created_at_utc, last_seen_at_utc, expires_at_utc
            FROM iam.sessions
            WHERE user_id = @userId AND is_revoked = false AND expires_at_utc > @nowUtc
            ORDER BY last_seen_at_utc DESC;");

        var pUser = cmd.CreateParameter(); pUser.ParameterName = "userId"; pUser.Value = userId; cmd.Parameters.Add(pUser);
        var pNow = cmd.CreateParameter(); pNow.ParameterName = "nowUtc"; pNow.Value = nowUtc; cmd.Parameters.Add(pNow);

        var list = new List<UserSessionSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var authMethodInt = reader.GetInt32(2);
            var authMethodStr = ((AuthenticationMethod)authMethodInt).ToString();
            list.Add(new UserSessionSummaryDto(
                SessionId: reader.GetGuid(0),
                TenantId: reader.GetGuid(1),
                AuthMethod: authMethodStr,
                CreatedAtUtc: reader.GetFieldValue<DateTimeOffset>(3),
                LastSeenAtUtc: reader.GetFieldValue<DateTimeOffset>(4),
                ExpiresAtUtc: reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return list;
    }
}
