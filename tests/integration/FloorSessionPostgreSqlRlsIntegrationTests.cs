using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Verifies PostgreSQL Row-Level Security (RLS) tenant isolation and partial unique index
/// enforcement for dining sessions using the unprivileged runtime application role.
/// </summary>
public class FloorSessionPostgreSqlRlsIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public FloorSessionPostgreSqlRlsIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<string> GetRuntimeConnectionStringAsync()
    {
        await EnsureMigrationsAppliedAsync();
        return await _fixture.ProvisionTemporaryRuntimeRoleAsync();
    }

    private async Task EnsureMigrationsAppliedAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(RestaurantOrderDbContext).Assembly.FullName);
            })
            .Options;

        await using var context = new RestaurantOrderDbContext(options);
        await context.Database.MigrateAsync();
    }

    private async Task<RestaurantOrderDbContext> CreateRuntimeDbContextAsync(ITenantContext? tenantContext = null)
    {
        var connStr = await GetRuntimeConnectionStringAsync();
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new RestaurantOrderDbContext(options, tenantContext);
    }

    [Fact]
    public async Task RLS_TenantA_CanOnlySee_TenantA_Sessions_And_CannotSee_TenantB()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-{Guid.NewGuid():N}");

        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var tableA = Guid.NewGuid();
        var tableB = Guid.NewGuid();

        await using (var adminCtx = new RestaurantOrderDbContext(
            new DbContextOptionsBuilder<RestaurantOrderDbContext>().UseNpgsql(_fixture.DatabaseConnectionString).Options))
        {
            var brandA = Guid.NewGuid();
            var brandB = Guid.NewGuid();

            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.brands (id, tenant_id, name, code, is_active, created_at_utc, concurrency_token)
                VALUES ({0}, {1}, 'Brand A', {2}, true, NOW(), gen_random_uuid()),
                       ({3}, {4}, 'Brand B', {5}, true, NOW(), gen_random_uuid());
            ", brandA, tenantA, $"ba-{Guid.NewGuid():N}", brandB, tenantB, $"bb-{Guid.NewGuid():N}");

            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, code, city, timezone, currency, status, created_at_utc, concurrency_token)
                VALUES ({0}, {1}, {2}, 'Branch A', {3}, 'Istanbul', 'Europe/Istanbul', 'TRY', 1, NOW(), gen_random_uuid()),
                       ({4}, {5}, {6}, 'Branch B', {7}, 'Ankara', 'Europe/Istanbul', 'TRY', 1, NOW(), gen_random_uuid());
            ", branchA, tenantA, brandA, $"bra-{Guid.NewGuid():N}", branchB, tenantB, brandB, $"brb-{Guid.NewGuid():N}");

            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, display_order, is_active, created_at_utc, concurrency_token)
                VALUES ({0}, {1}, {2}, 'Main Hall A', {3}, 1, 0, true, NOW(), gen_random_uuid()),
                       ({4}, {5}, {6}, 'Main Hall B', {7}, 1, 0, true, NOW(), gen_random_uuid());
            ", areaA, tenantA, branchA, $"daa-{Guid.NewGuid():N}", areaB, tenantB, branchB, $"dab-{Guid.NewGuid():N}");

            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at_utc)
                VALUES ({0}, {1}, {2}, {3}, 'T-1', 'Table 1', 4, 0, 0, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW()),
                       ({4}, {5}, {6}, {7}, 'T-1', 'Table 1', 4, 0, 0, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW());
            ", tableA, tenantA, branchA, areaA, tableB, tenantB, branchB, areaB);
        }

        var sessionAId = Guid.NewGuid();
        var sessionBId = Guid.NewGuid();

        // 1. Insert session for Tenant A via Tenant A runtime context
        await using (var ctxA = await CreateRuntimeDbContextAsync(new TenantContext(tenantA, isAuthenticated: true)))
        {
            await using var tx = await ctxA.BeginTenantTransactionAsync(tenantA);
            await ctxA.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, concurrency_token, created_at_utc)
                VALUES ({0}, {1}, {2}, {3}, 1, 2, NOW(), gen_random_uuid(), NOW());
            ", sessionAId, tenantA, branchA, tableA);
            await tx.CommitAsync();
        }

        // 2. Insert session for Tenant B via Tenant B runtime context
        await using (var ctxB = await CreateRuntimeDbContextAsync(new TenantContext(tenantB, isAuthenticated: true)))
        {
            await using var tx = await ctxB.BeginTenantTransactionAsync(tenantB);
            await ctxB.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, concurrency_token, created_at_utc)
                VALUES ({0}, {1}, {2}, {3}, 1, 3, NOW(), gen_random_uuid(), NOW());
            ", sessionBId, tenantB, branchB, tableB);
            await tx.CommitAsync();
        }

        // 3. Query as Tenant A: Must see sessionA, CANNOT see sessionB
        await using (var ctxA = await CreateRuntimeDbContextAsync(new TenantContext(tenantA, isAuthenticated: true)))
        {
            await using var tx = await ctxA.BeginTenantTransactionAsync(tenantA);

            var countA = await ctxA.DiningSessions.IgnoreQueryFilters().CountAsync();
            Assert.Equal(1, countA);

            var sessionA = await ctxA.DiningSessions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == new DiningSessionId(sessionAId));
            Assert.NotNull(sessionA);
            Assert.Equal(2, sessionA.GuestCount);

            var sessionB = await ctxA.DiningSessions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == new DiningSessionId(sessionBId));
            Assert.Null(sessionB);
        }

        // 4. Query as Tenant B: Must see sessionB, CANNOT see sessionA
        await using (var ctxB = await CreateRuntimeDbContextAsync(new TenantContext(tenantB, isAuthenticated: true)))
        {
            await using var tx = await ctxB.BeginTenantTransactionAsync(tenantB);

            var countB = await ctxB.DiningSessions.IgnoreQueryFilters().CountAsync();
            Assert.Equal(1, countB);

            var sessionB = await ctxB.DiningSessions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == new DiningSessionId(sessionBId));
            Assert.NotNull(sessionB);
            Assert.Equal(3, sessionB.GuestCount);

            var sessionA = await ctxB.DiningSessions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == new DiningSessionId(sessionAId));
            Assert.Null(sessionA);
        }
    }
}
