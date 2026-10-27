using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RestaurantOrder.Infrastructure.Persistence;

// Her kayıt kendi tenant transaction'ında işlenir. Satır kilidi yeniden claim ile
// yarışmayı engeller; süre kontrolü geç dönen sağlayıcının durumu yazmasını önler.
internal sealed class WorkerClaimTransaction : IAsyncDisposable
{
    private readonly RestaurantOrderDbContext _db;
    private readonly IDbContextTransaction? _transaction;
    private readonly DateTimeOffset _deadline;
    public bool IsValid { get; private set; }

    private WorkerClaimTransaction(RestaurantOrderDbContext db, IDbContextTransaction? transaction, DateTimeOffset deadline)
    {
        _db = db;
        _transaction = transaction;
        _deadline = deadline;
    }

    public static async Task<WorkerClaimTransaction> BeginAsync(RestaurantOrderDbContext db, Guid tenantId,
        Guid id, string claimId, bool push, int seconds, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) throw new InvalidOperationException("Worker tenant is required.");
        var tx = db.Database.IsRelational() ? await db.BeginTenantTransactionAsync(tenantId, cancellationToken: ct) : null;
        var scope = new WorkerClaimTransaction(db, tx, DateTimeOffset.UtcNow.AddSeconds(seconds - 2));
        try
        {
            if (db.Database.IsNpgsql())
            {
                var table = push ? "notification_deliveries" : "operational_outbox";
                var status = push ? 1 : 2;
                var sql = $"UPDATE tenancy.{table} SET lock_expires_utc = clock_timestamp() + make_interval(secs => @seconds) " +
                    "WHERE id = @id AND tenant_id = @tenant AND lock_claim_id = @claim AND status = @status AND lock_expires_utc > clock_timestamp()";
                await using var cmd = db.Database.GetDbConnection().CreateCommand();
                cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
                cmd.CommandText = sql;
                foreach (var (key, value) in new (string, object)[] { ("seconds", seconds), ("id", id), ("tenant", tenantId), ("claim", claimId), ("status", status) })
                {
                    var p = cmd.CreateParameter(); p.ParameterName = key; p.Value = value; cmd.Parameters.Add(p);
                }
                scope.IsValid = await cmd.ExecuteNonQueryAsync(ct) == 1;
            }
            else scope.IsValid = true;
            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    public void EnsureValid(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsValid || DateTimeOffset.UtcNow >= _deadline) throw new OperationCanceledException("Worker claim expired.");
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        EnsureValid(ct);
        if (_transaction != null) await _transaction.CommitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction != null) await _transaction.DisposeAsync();
        _db.ChangeTracker.Clear();
    }
}
