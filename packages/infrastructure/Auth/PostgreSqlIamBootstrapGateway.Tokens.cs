using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

public sealed partial class PostgreSqlIamBootstrapGateway
{
    public async Task<IReadOnlyList<UserActiveMembershipSummaryDto>> LookupActiveMembershipsByUserIdAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty) return Array.Empty<UserActiveMembershipSummaryDto>();

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT membership_id, tenant_id, branch_id, role FROM iam.lookup_active_memberships_by_user(@userId);");

        var param = cmd.CreateParameter();
        param.ParameterName = "userId";
        param.Value = userId;
        cmd.Parameters.Add(param);

        var list = new List<UserActiveMembershipSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var branchId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
            list.Add(new UserActiveMembershipSummaryDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                branchId,
                reader.GetString(3)));
        }

        return list;
    }

    public async Task<ConsumedInvitationTokenDto?> ConsumeInvitationTokenAtomicallyAsync(
        string tokenHash,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT invitation_id, tenant_id, user_id FROM iam.consume_invitation_token(@tokenHash, @nowUtc);");

        var pToken = cmd.CreateParameter();
        pToken.ParameterName = "tokenHash";
        pToken.Value = tokenHash;
        cmd.Parameters.Add(pToken);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new ConsumedInvitationTokenDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2));
        }

        return null;
    }

    public async Task<ConsumedPasswordResetTokenDto?> ConsumePasswordResetTokenAtomicallyAsync(
        string tokenHash,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT reset_token_id, tenant_id, user_id FROM iam.consume_password_reset_token(@tokenHash, @nowUtc);");

        var pToken = cmd.CreateParameter();
        pToken.ParameterName = "tokenHash";
        pToken.Value = tokenHash;
        cmd.Parameters.Add(pToken);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new ConsumedPasswordResetTokenDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2));
        }

        return null;
    }

    public async Task<ConsumedInvitationTokenDto?> ConsumeInvitationTokenInTransactionAsync(
        DbConnection connection,
        DbTransaction transaction,
        string tokenHash,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return null;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT invitation_id, tenant_id, user_id FROM iam.consume_invitation_token(@tokenHash, @nowUtc);";

        var pToken = cmd.CreateParameter();
        pToken.ParameterName = "tokenHash";
        pToken.Value = tokenHash;
        cmd.Parameters.Add(pToken);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new ConsumedInvitationTokenDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2));
        }

        return null;
    }

    public async Task ActivateUserAndSetPasswordInTransactionAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid userId,
        string passwordHash,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            UPDATE iam.users
            SET status = 2,
                password_hash = @passwordHash,
                security_version = security_version + 1,
                lockout_end_utc = NULL,
                failed_login_attempts = 0,
                updated_at_utc = @nowUtc,
                concurrency_token = gen_random_uuid()
            WHERE id = @userId;";

        var pPass = cmd.CreateParameter();
        pPass.ParameterName = "passwordHash";
        pPass.Value = passwordHash;
        cmd.Parameters.Add(pPass);

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;
        cmd.Parameters.Add(pUser);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ConsumedPasswordResetTokenDto?> ConsumePasswordResetTokenInTransactionAsync(
        DbConnection connection,
        DbTransaction transaction,
        string tokenHash,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return null;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT reset_token_id, tenant_id, user_id FROM iam.consume_password_reset_token(@tokenHash, @nowUtc);";

        var pToken = cmd.CreateParameter();
        pToken.ParameterName = "tokenHash";
        pToken.Value = tokenHash;
        cmd.Parameters.Add(pToken);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new ConsumedPasswordResetTokenDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2));
        }

        return null;
    }

    public async Task ResetUserPasswordInTransactionAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid userId,
        string passwordHash,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            UPDATE iam.users
            SET password_hash = @passwordHash,
                security_version = security_version + 1,
                lockout_end_utc = NULL,
                failed_login_attempts = 0,
                updated_at_utc = @nowUtc,
                concurrency_token = gen_random_uuid()
            WHERE id = @userId;";

        var pPass = cmd.CreateParameter();
        pPass.ParameterName = "passwordHash";
        pPass.Value = passwordHash;
        cmd.Parameters.Add(pPass);

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;
        cmd.Parameters.Add(pUser);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
