using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RestaurantOrder.Api.Tenancy;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Catalog;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class CatalogAvailabilityTransactionRollbackIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public CatalogAvailabilityTransactionRollbackIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private RestaurantOrderDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        var tenantContext = new TenantContext(tenantId, isAuthenticated: true);
        return new RestaurantOrderDbContext(options, tenantContext);
    }

    private async Task SeedBranchAsync(Guid tenantId, Guid branchId, string name, string slug)
    {
        var brandId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        var brandSql = @"
            INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
            VALUES (@id, @tenantId, @name, @slug, 'Active', NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmd = new NpgsqlCommand(brandSql, conn))
        {
            cmd.Parameters.AddWithValue("id", brandId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("name", $"{name} Brand");
            cmd.Parameters.AddWithValue("slug", $"brand-{slug}");
            cmd.Parameters.AddWithValue("token", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }

        var branchSql = @"
            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
            VALUES (@id, @tenantId, @brandId, @name, @slug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmd = new NpgsqlCommand(branchSql, conn))
        {
            cmd.Parameters.AddWithValue("id", branchId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("brandId", brandId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("slug", slug);
            cmd.Parameters.AddWithValue("token", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static AuthenticatedPrincipal CreateAdminActor(Guid tenantId)
    {
        return new AuthenticatedPrincipal(
            subjectId: Guid.NewGuid(),
            principalType: PrincipalType.Staff,
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(new TenantId(tenantId)),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);
    }

    [Fact]
    public async Task WhenResponseIs500_AvailabilityAndOutboxRollbackTransactionally()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Rollback Tenant", $"rt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Rollback Branch", $"rb-{Guid.NewGuid():N}");

        var tId = new TenantId(tenantId);
        var bId = new BranchId(branchId);

        // Seed initial menu and item
        Guid itemId;
        Guid menuId;
        Guid initialToken;
        await using (var db = CreateDbContext(tenantId))
        {
            var menu = Menu.Create(tId, bId, "Menu", $"rm-{Guid.NewGuid():N}");
            db.Menus.Add(menu);
            var cat = MenuCategory.Create(tId, bId, menu.Id, "Cat", $"rc-{Guid.NewGuid():N}");
            db.MenuCategories.Add(cat);
            var item = MenuItem.Create(tId, bId, menu.Id, cat.Id, "Item", $"ri-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(1000));
            db.MenuItems.Add(item);
            await db.SaveChangesAsync();
            menuId = menu.Id.Value;
            itemId = item.Id.Value;
            initialToken = item.ConcurrencyToken;
        }

        var mockAccessor = new Mock<ITenantContextAccessor>();
        mockAccessor.Setup(a => a.TenantContext).Returns(new TenantContext(tenantId, isAuthenticated: true));

        // Pipeline execution: perform Quick86, SaveChangesAsync, but then downstream middleware / endpoint throws or sets 500
        RequestDelegate next = async ctx =>
        {
            var db = ctx.RequestServices?.GetService(typeof(RestaurantOrderDbContext)) as RestaurantOrderDbContext
                     ?? (RestaurantOrderDbContext)ctx.Items["DbContext"]!;

            var catalogService = new CatalogService(db, NullLogger<CatalogService>.Instance);
            var actor = CreateAdminActor(tenantId);

            await catalogService.Quick86ItemAsync(
                tId,
                bId,
                new MenuId(menuId),
                new MenuItemId(itemId),
                new Quick86ItemCommand("SoldOut", ConcurrencyToken: initialToken),
                actor,
                CancellationToken.None);

            // Simulate downstream failure / 500 status code
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        };

        var middleware = new TenantTransactionMiddleware(next, NullLogger<TenantTransactionMiddleware>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = $"/api/v1/catalog/branches/{branchId}/menus";

        await using (var dbContext = CreateDbContext(tenantId))
        {
            httpContext.Items["DbContext"] = dbContext;
            await middleware.InvokeAsync(httpContext, mockAccessor.Object, dbContext);
        }

        // Verify that after 500, transaction was rolled back:
        // 1. Availability record does NOT exist
        // 2. Outbox record does NOT exist
        await using (var verifyDb = CreateDbContext(tenantId))
        {
            var avail = await verifyDb.BranchItemAvailabilities
                .FirstOrDefaultAsync(a => a.MenuItemId == new MenuItemId(itemId));
            Assert.Null(avail);

            var outbox = await verifyDb.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == itemId.ToString())
                .ToListAsync();
            Assert.Empty(outbox);
        }
    }

    [Fact]
    public async Task WhenExceptionThrown_AvailabilityAndOutboxRollbackTransactionally()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Ex Rollback Tenant", $"ert-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Ex Rollback Branch", $"erb-{Guid.NewGuid():N}");

        var tId = new TenantId(tenantId);
        var bId = new BranchId(branchId);

        Guid itemId;
        Guid menuId;
        Guid initialToken;
        await using (var db = CreateDbContext(tenantId))
        {
            var menu = Menu.Create(tId, bId, "Menu", $"erm-{Guid.NewGuid():N}");
            db.Menus.Add(menu);
            var cat = MenuCategory.Create(tId, bId, menu.Id, "Cat", $"erc-{Guid.NewGuid():N}");
            db.MenuCategories.Add(cat);
            var item = MenuItem.Create(tId, bId, menu.Id, cat.Id, "Item", $"eri-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(1000));
            db.MenuItems.Add(item);
            await db.SaveChangesAsync();
            menuId = menu.Id.Value;
            itemId = item.Id.Value;
            initialToken = item.ConcurrencyToken;
        }

        var mockAccessor = new Mock<ITenantContextAccessor>();
        mockAccessor.Setup(a => a.TenantContext).Returns(new TenantContext(tenantId, isAuthenticated: true));

        RequestDelegate next = async ctx =>
        {
            var db = (RestaurantOrderDbContext)ctx.Items["DbContext"]!;
            var catalogService = new CatalogService(db, NullLogger<CatalogService>.Instance);
            var actor = CreateAdminActor(tenantId);

            await catalogService.Quick86ItemAsync(
                tId,
                bId,
                new MenuId(menuId),
                new MenuItemId(itemId),
                new Quick86ItemCommand("SoldOut", ConcurrencyToken: initialToken),
                actor,
                CancellationToken.None);

            throw new InvalidOperationException("Simulated catastrophic crash after Quick86 SaveChangesAsync");
        };

        var middleware = new TenantTransactionMiddleware(next, NullLogger<TenantTransactionMiddleware>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = $"/api/v1/catalog/branches/{branchId}/menus";

        await using (var dbContext = CreateDbContext(tenantId))
        {
            httpContext.Items["DbContext"] = dbContext;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                middleware.InvokeAsync(httpContext, mockAccessor.Object, dbContext));
        }

        // Verify that after exception, transaction was rolled back:
        await using (var verifyDb = CreateDbContext(tenantId))
        {
            var avail = await verifyDb.BranchItemAvailabilities
                .FirstOrDefaultAsync(a => a.MenuItemId == new MenuItemId(itemId));
            Assert.Null(avail);

            var outbox = await verifyDb.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == itemId.ToString())
                .ToListAsync();
            Assert.Empty(outbox);
        }
    }
}
