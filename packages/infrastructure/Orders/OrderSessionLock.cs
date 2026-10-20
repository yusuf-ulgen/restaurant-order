using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Orders;

internal static class OrderSessionLock
{
    public static async Task AcquireAsync(RestaurantOrderDbContext db, TenantId tenantId,
        BranchId branchId, DiningSessionId sessionId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql()) return;

        // Sipariş turları kendi arasında sıralanır; oturum satırı ise kapanış,
        // hesap isteme, taşıma ve birleştirmeyle ortak işlem sınırını sağlar.
        var lockKey = $"order_session_{sessionId.Value:D}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM tenancy.dining_sessions WHERE tenant_id = {tenantId.Value} AND branch_id = {branchId.Value} AND id = {sessionId.Value} FOR UPDATE",
            cancellationToken);
    }
}
