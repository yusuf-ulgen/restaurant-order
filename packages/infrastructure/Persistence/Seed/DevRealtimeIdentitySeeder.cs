using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Seed;

// Yalnızca DevDataSeeder'ın Development sınırı içinde çağrılır.
internal static class DevRealtimeIdentitySeeder
{
    internal static async Task SeedAsync(RestaurantOrderDbContext db, IEnumerable<(TenantId Tenant, BranchId Branch)> branches, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (tenant, branch) in branches)
        foreach (var role in new[] { AuthRole.Waiter, AuthRole.Cashier, AuthRole.Kitchen, AuthRole.Bar, AuthRole.BranchManager })
        {
            Guid Id(string kind) => Guid.Parse(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenant.Value:D}:{branch.Value:D}:{role}:{kind}")))[..32]);
            var user = new UserId(Id("user"));
            var membership = Id("membership");
            var session = Id("session");
            if (!await db.Users.AnyAsync(u => u.Id == user, ct))
                db.Users.Add(User.Create($"synthetic-{user.Value:N}@example.invalid", "insecure-dev-hash-for-local-development-only", now, user));
            if (!await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.Id == membership, ct))
                db.Memberships.Add(UserMembership.Create(tenant, user, role, branch, now, membership));
            if (!await db.Sessions.IgnoreQueryFilters().AnyAsync(s => s.Id == session, ct))
                db.Sessions.Add(AuthSession.Create(tenant, user, membership, branch, AuthenticationMethod.Password,
                    TimeSpan.FromDays(3650), now, id: session));
        }
    }
}
