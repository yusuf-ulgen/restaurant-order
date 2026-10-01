using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// PostgreSQL implementation of IIamUserLookupGateway.
/// Calls the dedicated SECURITY DEFINER function iam.lookup_user_for_login(text)
/// with search_path pinned to (iam, pg_temp), adhering strictly to ADR-0010.
/// </summary>
public sealed class PostgreSqlIamUserLookupGateway : IIamUserLookupGateway
{
    private readonly RestaurantOrderDbContext _dbContext;

    public PostgreSqlIamUserLookupGateway(RestaurantOrderDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<UserLoginLookupDto?> LookupUserForLoginAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return null;
        }

        var conn = _dbContext.Database.GetDbConnection();
        var wasClosed = conn.State == ConnectionState.Closed;
        if (wasClosed)
        {
            await conn.OpenAsync(cancellationToken);
        }

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = "SELECT user_id, normalized_email, password_hash, status, security_version, lockout_end_utc " +
                             "FROM iam.lookup_user_for_login(@normalizedEmail);";

            var param = cmd.CreateParameter();
            param.ParameterName = "normalizedEmail";
            param.Value = normalizedEmail.Trim().ToLowerInvariant();
            cmd.Parameters.Add(param);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var userId = reader.GetGuid(0);
                var email = reader.GetString(1);
                var passwordHash = reader.GetString(2);
                var status = reader.GetInt32(3);
                var securityVersion = reader.GetInt32(4);
                DateTimeOffset? lockoutEndUtc = reader.IsDBNull(5) ? null : reader.GetDateTime(5);

                return new UserLoginLookupDto(
                    userId,
                    email,
                    passwordHash,
                    status,
                    securityVersion,
                    lockoutEndUtc);
            }

            return null;
        }
        finally
        {
            if (wasClosed)
            {
                await conn.CloseAsync();
            }
        }
    }
}
