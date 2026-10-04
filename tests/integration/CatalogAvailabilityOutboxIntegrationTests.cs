using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class CatalogAvailabilityOutboxIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public CatalogAvailabilityOutboxIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
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

    private RestaurantOrderDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;

        return new RestaurantOrderDbContext(options, new TenantContext(tenantId, isAuthenticated: true));
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

    [Fact]
    public async Task Quick86_And_Restock_PersistOutboxMessages_Transactionally()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Outbox Tenant", $"ot-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Outbox Branch", $"ob-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // 1. Create Menu, Category, and Item
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("Outbox Menu", $"om-{Guid.NewGuid():N}"));
        var menu = await (await client.SendAsync(menuReq)).Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("Outbox Cat", $"oc-{Guid.NewGuid():N}"));
        var cat = await (await client.SendAsync(catReq)).Content.ReadFromJsonAsync<MenuCategoryDto>();

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(cat!.Id, "Outbox Soup", $"os-{Guid.NewGuid():N}", 4500));
        var item = await (await client.SendAsync(itemReq)).Content.ReadFromJsonAsync<MenuItemDto>();

        // 2. Perform Quick86
        var q86Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item!.Id}/quick-86", adminToken);
        q86Req.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: item.ConcurrencyToken));
        var q86Resp = await client.SendAsync(q86Req);
        Assert.Equal(HttpStatusCode.OK, q86Resp.StatusCode);

        // Verify exactly 1 outbox message written for Quick86
        await using (var db = CreateDbContext(tenantId))
        {
            var targetId = item.Id.ToString();
            var outboxMessages = await db.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == targetId && m.EventType == "CatalogItemQuick86")
                .ToListAsync();

            Assert.Single(outboxMessages);
            var msg = outboxMessages[0];
            Assert.Equal(tenantId, msg.TenantId.Value);
            Assert.Equal(branchId, msg.BranchId.Value);
            Assert.Equal(1, msg.SchemaVersion);
            Assert.Equal(CatalogOutboxStatus.Pending, msg.Status);
            Assert.Contains(item.Id.ToString(), msg.Payload);
            Assert.Contains("SoldOut", msg.Payload);
        }

        // 3. Perform Restock
        var availReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/availability", adminToken);
        var availList = await (await client.SendAsync(availReq)).Content.ReadFromJsonAsync<List<BranchItemAvailabilityDto>>();
        var avail = availList!.Single(a => a.MenuItemId == item.Id && a.ItemVariantId == null);

        var restockReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/restock", adminToken);
        restockReq.Content = JsonContent.Create(new RestockItemCommand("Available again", avail.ConcurrencyToken));
        var restockResp = await client.SendAsync(restockReq);
        Assert.Equal(HttpStatusCode.OK, restockResp.StatusCode);

        // Verify restock outbox message was written
        await using (var db = CreateDbContext(tenantId))
        {
            var targetId = item.Id.ToString();
            var outboxMessages = await db.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == targetId && m.EventType == "CatalogItemRestocked")
                .ToListAsync();

            Assert.Single(outboxMessages);
            var msg = outboxMessages[0];
            Assert.Equal(CatalogOutboxStatus.Pending, msg.Status);
            Assert.Contains("Available again", msg.Payload);
        }

        // 4. Failed request with stale concurrency token (409) must NOT write outbox message
        var staleReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/quick-86", adminToken);
        staleReq.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: Guid.NewGuid()));
        var staleResp = await client.SendAsync(staleReq);
        Assert.Equal(HttpStatusCode.Conflict, staleResp.StatusCode);

        await using (var db = CreateDbContext(tenantId))
        {
            var targetId = item.Id.ToString();
            var totalMessages = await db.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == targetId)
                .CountAsync();

            // Total messages must still be exactly 2 (1 Quick86 + 1 Restocked)
            Assert.Equal(2, totalMessages);
        }
    }
}
