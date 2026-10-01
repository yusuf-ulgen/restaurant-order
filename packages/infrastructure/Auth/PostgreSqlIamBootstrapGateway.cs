using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// PostgreSQL implementation of IIamBootstrapGateway.
/// Calls dedicated SECURITY DEFINER functions with pinned search_paths and executes
/// column-level updates on iam.users, strictly adhering to ADR-0010 without requiring blanket SELECT.
/// </summary>
public sealed class PostgreSqlIamBootstrapGateway : IIamBootstrapGateway
{
    private readonly RestaurantOrderDbContext _dbContext;

    public PostgreSqlIamBootstrapGateway(RestaurantOrderDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    private async Task<DbConnection> GetOpenConnectionAsync(CancellationToken ct)
    {
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }
        return conn;
    }

    private DbCommand CreateCommand(DbConnection conn, string commandText)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = commandText;
        return cmd;
    }

    public async Task<TenantLookupDto?> LookupTenantBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT tenant_id, name, slug, status FROM iam.lookup_tenant_by_slug(@slug);");

        var param = cmd.CreateParameter();
        param.ParameterName = "slug";
        param.Value = slug.Trim().ToLowerInvariant();
        cmd.Parameters.Add(param);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new TenantLookupDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3));
        }

        return null;
    }

    public async Task<MembershipLookupDto?> LookupMembershipForLoginAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT membership_id, tenant_id, user_id, role, branch_id, is_active FROM iam.lookup_membership_for_login(@tenantId, @userId);");

        var pTenant = cmd.CreateParameter();
        pTenant.ParameterName = "tenantId";
        pTenant.Value = tenantId;
        cmd.Parameters.Add(pTenant);

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;
        cmd.Parameters.Add(pUser);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var branchId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
            return new MembershipLookupDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                branchId,
                reader.GetBoolean(5));
        }

        return null;
    }

    public async Task<InvitationTokenLookupDto?> LookupInvitationTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT invitation_id, tenant_id, user_id, expires_at_utc, is_consumed FROM iam.lookup_invitation_token(@tokenHash);");

        var param = cmd.CreateParameter();
        param.ParameterName = "tokenHash";
        param.Value = tokenHash.Trim();
        cmd.Parameters.Add(param);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new InvitationTokenLookupDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetBoolean(4));
        }

        return null;
    }

    public async Task<PasswordResetTokenLookupDto?> LookupPasswordResetTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT reset_token_id, tenant_id, user_id, expires_at_utc, is_consumed FROM iam.lookup_password_reset_token(@tokenHash);");

        var param = cmd.CreateParameter();
        param.ParameterName = "tokenHash";
        param.Value = tokenHash.Trim();
        cmd.Parameters.Add(param);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new PasswordResetTokenLookupDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetBoolean(4));
        }

        return null;
    }

    public async Task<TerminalAuthLookupDto?> LookupTerminalForAuthAsync(Guid terminalId, CancellationToken ct = default)
    {
        if (terminalId == Guid.Empty) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT terminal_id, tenant_id, branch_id, device_identifier, terminal_name, secret_hash, is_active FROM iam.lookup_terminal_for_auth(@terminalId);");

        var param = cmd.CreateParameter();
        param.ParameterName = "terminalId";
        param.Value = terminalId;
        cmd.Parameters.Add(param);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new TerminalAuthLookupDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6));
        }

        return null;
    }

    public async Task<UserActiveMembershipSummaryDto?> LookupFirstActiveMembershipByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT membership_id, tenant_id, branch_id, role FROM iam.lookup_first_active_membership_by_user(@userId);");

        var param = cmd.CreateParameter();
        param.ParameterName = "userId";
        param.Value = userId;
        cmd.Parameters.Add(param);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var branchId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
            return new UserActiveMembershipSummaryDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                branchId,
                reader.GetString(3));
        }

        return null;
    }

    public async Task RecordSuccessfulLoginAsync(Guid userId, DateTimeOffset nowUtc, string? newPasswordHash = null, CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, @"
            UPDATE iam.users
            SET last_login_at_utc = @nowUtc,
                failed_login_attempts = 0,
                lockout_end_utc = NULL,
                password_hash = COALESCE(@newPasswordHash, password_hash),
                updated_at_utc = @nowUtc,
                concurrency_token = gen_random_uuid()
            WHERE id = @userId;");

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;
        cmd.Parameters.Add(pUser);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        var pHash = cmd.CreateParameter();
        pHash.ParameterName = "newPasswordHash";
        pHash.Value = (object?)newPasswordHash ?? DBNull.Value;
        cmd.Parameters.Add(pHash);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordFailedLoginAttemptAsync(Guid userId, int failedAttempts, DateTimeOffset? lockoutEndUtc, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, $@"
            UPDATE iam.users
            SET failed_login_attempts = @failedAttempts,
                lockout_end_utc = @lockoutEndUtc,
                status = CASE WHEN @lockoutEndUtc IS NOT NULL THEN {(int)UserStatus.Locked} ELSE status END,
                updated_at_utc = @nowUtc,
                concurrency_token = gen_random_uuid()
            WHERE id = @userId;");

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;
        cmd.Parameters.Add(pUser);

        var pAttempts = cmd.CreateParameter();
        pAttempts.ParameterName = "failedAttempts";
        pAttempts.Value = failedAttempts;
        cmd.Parameters.Add(pAttempts);

        var pLockout = cmd.CreateParameter();
        pLockout.ParameterName = "lockoutEndUtc";
        pLockout.Value = (object?)lockoutEndUtc ?? DBNull.Value;
        cmd.Parameters.Add(pLockout);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ActivateUserAndSetPasswordAsync(Guid userId, string passwordHash, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, $@"
            UPDATE iam.users
            SET status = {(int)UserStatus.Active},
                password_hash = @passwordHash,
                security_version = security_version + 1,
                failed_login_attempts = 0,
                lockout_end_utc = NULL,
                updated_at_utc = @nowUtc,
                concurrency_token = gen_random_uuid()
            WHERE id = @userId;");

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;
        cmd.Parameters.Add(pUser);

        var pHash = cmd.CreateParameter();
        pHash.ParameterName = "passwordHash";
        pHash.Value = passwordHash;
        cmd.Parameters.Add(pHash);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ResetUserPasswordAsync(Guid userId, string passwordHash, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn, @"
            UPDATE iam.users
            SET password_hash = @passwordHash,
                security_version = security_version + 1,
                failed_login_attempts = 0,
                lockout_end_utc = NULL,
                updated_at_utc = @nowUtc,
                concurrency_token = gen_random_uuid()
            WHERE id = @userId;");

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;
        cmd.Parameters.Add(pUser);

        var pHash = cmd.CreateParameter();
        pHash.ParameterName = "passwordHash";
        pHash.Value = passwordHash;
        cmd.Parameters.Add(pHash);

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "nowUtc";
        pNow.Value = nowUtc;
        cmd.Parameters.Add(pNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<UserSummaryLookupDto?> LookupUserByIdAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT user_id, email, normalized_email, status, security_version, lockout_end_utc FROM iam.lookup_user_by_id(@userId);");

        var param = cmd.CreateParameter();
        param.ParameterName = "userId";
        param.Value = userId;
        cmd.Parameters.Add(param);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var lockout = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5);
            return new UserSummaryLookupDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                lockout);
        }

        return null;
    }

    public async Task<SessionTenantLookupDto?> LookupSessionTenantAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (sessionId == Guid.Empty) return null;

        var conn = await GetOpenConnectionAsync(ct);
        await using var cmd = CreateCommand(conn,
            "SELECT session_id, tenant_id FROM iam.lookup_session_tenant(@sessionId);");

        var param = cmd.CreateParameter();
        param.ParameterName = "sessionId";
        param.Value = sessionId;
        cmd.Parameters.Add(param);

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
