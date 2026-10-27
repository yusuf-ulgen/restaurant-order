using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Api.Realtime;

public interface IRealtimeConnectionAuthorizer
{
    Task<bool> IsAuthorizedAsync(TrackedConnection connection, CancellationToken ct = default);
}

public sealed class RealtimeConnectionAuthorizer(IServiceScopeFactory scopes) : IRealtimeConnectionAuthorizer
{
    public async Task<bool> IsAuthorizedAsync(TrackedConnection c, CancellationToken ct = default)
    {
        if (c.TokenExpiresAtUtc <= DateTimeOffset.UtcNow) return false;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RestaurantOrderDbContext>();
            await using var tx = await db.BeginTenantTransactionAsync(c.TenantId, cancellationToken: ct);
            var tenant = new TenantId(c.TenantId);
            var branch = new BranchId(c.BranchId);
            if (c.DiningSessionId.HasValue)
            {
                var session = new DiningSessionId(c.DiningSessionId.Value);
                return await db.DiningSessions.AsNoTracking().AnyAsync(s => s.TenantId == tenant && s.BranchId == branch &&
                    s.Id == session && s.Status != DiningSessionStatus.Closed, ct);
            }
            if (!c.StaffSessionId.HasValue || !c.UserId.HasValue || !c.SecurityVersion.HasValue) return false;
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            cmd.CommandText = "SELECT is_valid FROM iam.validate_token_session(@sid, @uid, @version)";
            foreach (var (name, value) in new (string, object)[] { ("sid", c.StaffSessionId.Value), ("uid", c.UserId.Value), ("version", c.SecurityVersion.Value) })
            {
                var p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value; cmd.Parameters.Add(p);
            }
            if (await cmd.ExecuteScalarAsync(ct) is not true) return false;
            var uid = new UserId(c.UserId.Value);
            if (!Enum.TryParse<AuthRole>(c.Role.Replace("_", ""), true, out var role)) return false;
            if (role == AuthRole.SuperAdmin) return await db.Branches.AnyAsync(b => b.Id == branch && b.TenantId == tenant, ct);
            return await db.Sessions.AsNoTracking().AnyAsync(s => s.Id == c.StaffSessionId && s.UserId == uid &&
                       s.TenantId == tenant && (s.BranchId == null || s.BranchId == branch), ct) &&
                   await db.Memberships.AsNoTracking().AnyAsync(m => m.TenantId == tenant && m.UserId == uid &&
                       (m.BranchId == null || m.BranchId == branch) && m.Status == UserMembershipStatus.Active && m.Role == role, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
    }
}
