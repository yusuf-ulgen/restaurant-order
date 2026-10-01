using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantOrder.Application.Tenancy;

namespace RestaurantOrder.Infrastructure.Persistence;

/// <summary>
/// Implements <see cref="ITenantDatabaseSession"/> to guarantee transaction-local tenant context propagation.
/// Uses PostgreSQL 'set_config(..., is_local => true)' to ensure tenant context does not leak across pooled connections.
/// </summary>
public class TenantDatabaseSession : ITenantDatabaseSession
{
    private readonly RestaurantOrderDbContext _context;

    public TenantDatabaseSession(RestaurantOrderDbContext context)
    {
        _context = context;
    }

    public async Task<IDbContextTransaction> BeginTenantTransactionAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new TenantContextException("Cannot begin tenant transaction with an empty Tenant ID. Fail-closed.");
        }

        var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var conn = _context.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction.GetDbTransaction();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";

        var param = cmd.CreateParameter();
        param.ParameterName = "tenantId";
        param.Value = tenantId.ToString();
        cmd.Parameters.Add(param);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        return transaction;
    }
}
