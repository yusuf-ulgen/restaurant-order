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
}
