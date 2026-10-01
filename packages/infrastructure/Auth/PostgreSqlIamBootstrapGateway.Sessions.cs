using System.Data;
using System.Data.Common;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

public sealed partial class PostgreSqlIamBootstrapGateway
{
    public Task<SessionTenantLookupDto?> LookupSessionTenantAsync(Guid sessionId, CancellationToken ct = default) =>
        LookupSessionTenantAsync(sessionId, null, ct);

    public async Task<SessionTenantLookupDto?> LookupSessionTenantAsync(Guid sessionId, Guid? userId, CancellationToken ct = default)
    {
        if (sessionId == Guid.Empty) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = userId.HasValue
            ? CreateCommand(conn, "SELECT session_id, tenant_id FROM iam.lookup_session_tenant_for_user(@sessionId, @userId);")
            : CreateCommand(conn, "SELECT session_id, tenant_id FROM iam.lookup_session_tenant(@sessionId);");

        var param = cmd.CreateParameter();
        param.ParameterName = "sessionId";
        param.Value = sessionId;
        cmd.Parameters.Add(param);

        if (userId.HasValue)
        {
            var pUser = cmd.CreateParameter();
            pUser.ParameterName = "userId";
            pUser.Value = userId.Value;
            cmd.Parameters.Add(pUser);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new SessionTenantLookupDto(
                reader.GetGuid(0),
                reader.GetGuid(1));
        }

        return null;
    }

    public async Task UpdateUserStatusAsync(Guid userId, UserStatus status, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        var lockoutEnd = status == UserStatus.Locked ? (object?)nowUtc.AddMinutes(15) : DBNull.Value;
        await using var cmd = CreateCommand(conn, $@"
            UPDATE iam.users
            SET status = {(int)status},
                security_version = security_version + 1,
                lockout_end_utc = @lockoutEnd,
                updated_at_utc = @nowUtc,
                concurrency_token = gen_random_uuid()
            WHERE id = @userId;");

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;
        cmd.Parameters.Add(pUser);

        var pLockout = cmd.CreateParameter();
        pLockout.ParameterName = "lockoutEnd";
        pLockout.Value = lockoutEnd;
        cmd.Parameters.Add(pLockout);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> RevokeMembershipSessionsAsync(
        Guid userId,
        Guid tenantId,
        Guid? branchId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, "SELECT session_id FROM iam.revoke_membership_sessions(@userId, @tenantId, @branchId, @nowUtc);");

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;
        cmd.Parameters.Add(pUser);

        var pTenant = cmd.CreateParameter();
        pTenant.ParameterName = "tenantId";
        pTenant.Value = tenantId;
        cmd.Parameters.Add(pTenant);

        var pBranch = cmd.CreateParameter();
        pBranch.ParameterName = "branchId";
        pBranch.Value = (object?)branchId ?? DBNull.Value;
        cmd.Parameters.Add(pBranch);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        var revokedSessionIds = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            revokedSessionIds.Add(reader.GetGuid(0));
        }

        return revokedSessionIds;
    }

    public async Task<IReadOnlyList<Guid>> RevokeAllUserSessionsInTransactionAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid userId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        // 1. Revoke matching refresh tokens
        await using (var cmdRt = connection.CreateCommand())
        {
            cmdRt.Transaction = transaction;
            cmdRt.CommandText = @"
                UPDATE iam.refresh_tokens rt
                SET is_revoked = true,
                    revoked_at_utc = @nowUtc
                FROM iam.sessions s
                WHERE rt.session_id = s.id
                  AND s.user_id = @userId
                  AND rt.is_revoked = false;";

            var pUser = cmdRt.CreateParameter();
            pUser.ParameterName = "userId";
            pUser.Value = userId;
            cmdRt.Parameters.Add(pUser);

            var pNow = cmdRt.CreateParameter();
            pNow.ParameterName = "nowUtc";
            pNow.Value = nowUtc;
            cmdRt.Parameters.Add(pNow);

            await cmdRt.ExecuteNonQueryAsync(ct);
        }

        // 2. Revoke platform sessions for user in the same transaction
        await using (var cmdPlatform = connection.CreateCommand())
        {
            cmdPlatform.Transaction = transaction;
            cmdPlatform.CommandText = "SELECT iam.revoke_all_user_platform_sessions(@userId, @nowUtc);";

            var pUser = cmdPlatform.CreateParameter();
            pUser.ParameterName = "userId";
            pUser.Value = userId;
            cmdPlatform.Parameters.Add(pUser);

            var pNow = cmdPlatform.CreateParameter();
            pNow.ParameterName = "nowUtc";
            pNow.Value = nowUtc;
            cmdPlatform.Parameters.Add(pNow);

            await cmdPlatform.ExecuteNonQueryAsync(ct);
        }

        // 3. Revoke matching sessions and return their IDs
        var revokedSessionIds = new List<Guid>();
        await using (var cmdSessions = connection.CreateCommand())
        {
            cmdSessions.Transaction = transaction;
            cmdSessions.CommandText = @"
                UPDATE iam.sessions
                SET is_revoked = true,
                    revocation_reason = 'password_reset',
                    revoked_at_utc = @nowUtc
                WHERE user_id = @userId
                  AND is_revoked = false
                RETURNING id;";

            var pUser = cmdSessions.CreateParameter();
            pUser.ParameterName = "userId";
            pUser.Value = userId;
            cmdSessions.Parameters.Add(pUser);

            var pNow = cmdSessions.CreateParameter();
            pNow.ParameterName = "nowUtc";
            pNow.Value = nowUtc;
            cmdSessions.Parameters.Add(pNow);

            await using var reader = await cmdSessions.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                revokedSessionIds.Add(reader.GetGuid(0));
            }
        }

        return revokedSessionIds;
    }
}
