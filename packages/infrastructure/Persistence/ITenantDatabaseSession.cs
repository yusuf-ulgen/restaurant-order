using Microsoft.EntityFrameworkCore.Storage;

namespace RestaurantOrder.Infrastructure.Persistence;

/// <summary>
/// Abstraction for managing tenant-scoped database transactions and sessions.
/// Guarantees that PostgreSQL session parameter 'app.current_tenant_id' is set transaction-locally
/// and cleaned up automatically upon transaction completion.
/// </summary>
public interface ITenantDatabaseSession
{
    /// <summary>
    /// Begins a database transaction and immediately binds it to the specified tenant.
    /// Uses transaction-local 'set_config' so the context is automatically cleared when the transaction ends.
    /// </summary>
    Task<IDbContextTransaction> BeginTenantTransactionAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
