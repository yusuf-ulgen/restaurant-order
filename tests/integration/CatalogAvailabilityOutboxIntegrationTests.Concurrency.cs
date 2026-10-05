using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public partial class CatalogAvailabilityOutboxIntegrationTests
{
    [Fact]
    public async Task Item_Quick86_ConcurrentSameTokenRequests_ProducesSingleOutboxAndDeterministicResult()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Conc Item Tenant", $"cit-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Conc Item Branch", $"cib-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // Create catalog hierarchy
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("Conc Menu", $"cm-{Guid.NewGuid():N}"));
        var menu = await (await client.SendAsync(menuReq)).Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("Conc Cat", $"cc-{Guid.NewGuid():N}"));
        var cat = await (await client.SendAsync(catReq)).Content.ReadFromJsonAsync<MenuCategoryDto>();

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(cat!.Id, "Conc Burger", $"cb-{Guid.NewGuid():N}", 5000));
        var item = await (await client.SendAsync(itemReq)).Content.ReadFromJsonAsync<MenuItemDto>();

        // Dispatch two concurrent Quick86 requests with the exact same valid initial token
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item!.Id}/quick-86", adminToken);
        req1.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: item.ConcurrencyToken));

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/quick-86", adminToken);
        req2.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: item.ConcurrencyToken));

        var responses = await Task.WhenAll(client.SendAsync(req1), client.SendAsync(req2));

        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statusCodes);
        Assert.Single(statusCodes, s => s == HttpStatusCode.OK);
        Assert.Single(statusCodes, s => s == HttpStatusCode.Conflict);

        // Verify database state: exactly one outbox message, exactly one availability record
        await using (var db = CreateDbContext(tenantId))
        {
            var targetId = item.Id.ToString();
            var outboxMessages = await db.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == targetId && m.EventType == "CatalogItemQuick86")
                .ToListAsync();

            Assert.Single(outboxMessages);
            var expectedKey = $"item-86-{tenantId}-{branchId}-{item.Id}-{item.ConcurrencyToken:D}";
            Assert.Equal(expectedKey, outboxMessages[0].IdempotencyKey);

            var availabilities = await db.BranchItemAvailabilities
                .Where(a => a.TenantId == new TenantId(tenantId) && a.BranchId == new BranchId(branchId) && a.MenuItemId == new MenuItemId(item.Id))
                .ToListAsync();
            Assert.Single(availabilities);
            Assert.False(availabilities[0].IsAvailable);
        }

        // Retry with same stale token sequentially: must return 409 Conflict without writing new outbox entry
        var retryReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/quick-86", adminToken);
        retryReq.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: item.ConcurrencyToken));
        var retryResp = await client.SendAsync(retryReq);
        Assert.Equal(HttpStatusCode.Conflict, retryResp.StatusCode);

        await using (var db = CreateDbContext(tenantId))
        {
            var targetId = item.Id.ToString();
            var outboxCount = await db.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == targetId)
                .CountAsync();
            Assert.Equal(1, outboxCount);
        }
    }

    [Fact]
    public async Task Variant_Quick86_ConcurrentSameTokenRequests_ProducesSingleOutboxAndDeterministicResult()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Conc Var Tenant", $"cvt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Conc Var Branch", $"cvb-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("Conc Var Menu", $"cvm-{Guid.NewGuid():N}"));
        var menu = await (await client.SendAsync(menuReq)).Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("Conc Var Cat", $"cvc-{Guid.NewGuid():N}"));
        var cat = await (await client.SendAsync(catReq)).Content.ReadFromJsonAsync<MenuCategoryDto>();

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(cat!.Id, "Conc Pizza", $"cpz-{Guid.NewGuid():N}", 6000));
        var item = await (await client.SendAsync(itemReq)).Content.ReadFromJsonAsync<MenuItemDto>();

        var varReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item!.Id}/variants", adminToken);
        varReq.Content = JsonContent.Create(new CreateItemVariantCommand("Large", "LRG", 7500, 1, false));
        var variant = await (await client.SendAsync(varReq)).Content.ReadFromJsonAsync<ItemVariantDto>();

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants/{variant!.Id}/quick-86", adminToken);
        req1.Content = JsonContent.Create(new Quick86VariantCommand("SoldOut", ConcurrencyToken: variant.ConcurrencyToken));

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants/{variant.Id}/quick-86", adminToken);
        req2.Content = JsonContent.Create(new Quick86VariantCommand("SoldOut", ConcurrencyToken: variant.ConcurrencyToken));

        var responses = await Task.WhenAll(client.SendAsync(req1), client.SendAsync(req2));

        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statusCodes);
        Assert.Single(statusCodes, s => s == HttpStatusCode.OK);
        Assert.Single(statusCodes, s => s == HttpStatusCode.Conflict);

        await using (var db = CreateDbContext(tenantId))
        {
            var targetId = variant.Id.ToString();
            var outboxMessages = await db.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == targetId && m.EventType == "CatalogVariantQuick86")
                .ToListAsync();

            Assert.Single(outboxMessages);
            var expectedKey = $"variant-86-{tenantId}-{branchId}-{variant.Id}-{variant.ConcurrencyToken:D}";
            Assert.Equal(expectedKey, outboxMessages[0].IdempotencyKey);
        }
    }

    [Fact]
    public async Task Restock_ConcurrentSameTokenRequests_ProducesSingleOutboxAndDeterministicResult()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Conc Restock Tenant", $"crt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Conc Restock Branch", $"crb-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("Restock Menu", $"rm-{Guid.NewGuid():N}"));
        var menu = await (await client.SendAsync(menuReq)).Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("Restock Cat", $"rc-{Guid.NewGuid():N}"));
        var cat = await (await client.SendAsync(catReq)).Content.ReadFromJsonAsync<MenuCategoryDto>();

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(cat!.Id, "Restock Lemonade", $"rl-{Guid.NewGuid():N}", 2500));
        var item = await (await client.SendAsync(itemReq)).Content.ReadFromJsonAsync<MenuItemDto>();

        // 1. Initial Quick86 to establish availability record
        var q86Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item!.Id}/quick-86", adminToken);
        q86Req.Content = JsonContent.Create(new Quick86ItemCommand("SoldOut", ConcurrencyToken: item.ConcurrencyToken));
        var q86Resp = await client.SendAsync(q86Req);
        Assert.Equal(HttpStatusCode.OK, q86Resp.StatusCode);

        // Fetch current availability concurrency token
        var availReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/availability", adminToken);
        var availList = await (await client.SendAsync(availReq)).Content.ReadFromJsonAsync<List<BranchItemAvailabilityDto>>();
        var avail = availList!.Single(a => a.MenuItemId == item.Id && a.ItemVariantId == null);

        // 2. Dispatch two concurrent Restock requests with same token
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/restock", adminToken);
        req1.Content = JsonContent.Create(new RestockItemCommand("Restocked batch", avail.ConcurrencyToken));

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/restock", adminToken);
        req2.Content = JsonContent.Create(new RestockItemCommand("Restocked batch", avail.ConcurrencyToken));

        var responses = await Task.WhenAll(client.SendAsync(req1), client.SendAsync(req2));

        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statusCodes);
        Assert.Single(statusCodes, s => s == HttpStatusCode.OK);
        Assert.Single(statusCodes, s => s == HttpStatusCode.Conflict);

        await using (var db = CreateDbContext(tenantId))
        {
            var targetId = item.Id.ToString();
            var restockMessages = await db.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == targetId && m.EventType == "CatalogItemRestocked")
                .ToListAsync();

            Assert.Single(restockMessages);
            var expectedKey = $"item-restock-{tenantId}-{branchId}-{item.Id}-{avail.ConcurrencyToken:D}";
            Assert.Equal(expectedKey, restockMessages[0].IdempotencyKey);
        }
    }

    [Fact]
    public async Task Outbox_UniqueConstraint_RejectsDuplicateIdempotencyKey_SameTenant()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var idempotencyKey = $"test-duplicate-key-{Guid.NewGuid():N}";

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Unique Tenant", $"ut-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Unique Branch", $"ub-{Guid.NewGuid():N}");

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        var sql = @"
            INSERT INTO tenancy.catalog_availability_outbox
                (id, tenant_id, branch_id, aggregate_id, event_type, payload, idempotency_key, occurred_at, created_at, status, attempt_count, max_attempts, schema_version)
            VALUES
                (@id, @tenantId, @branchId, 'agg-1', 'CatalogItemQuick86', '{}', @idemp, NOW(), NOW(), 0, 0, 5, 1);";

        // First insert succeeds
        await using (var cmd1 = new NpgsqlCommand(sql, conn))
        {
            cmd1.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd1.Parameters.AddWithValue("tenantId", tenantId);
            cmd1.Parameters.AddWithValue("branchId", branchId);
            cmd1.Parameters.AddWithValue("idemp", idempotencyKey);
            await cmd1.ExecuteNonQueryAsync();
        }

        // Second insert with same tenant and same idempotency key fails with unique constraint violation
        await using (var cmd2 = new NpgsqlCommand(sql, conn))
        {
            cmd2.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd2.Parameters.AddWithValue("tenantId", tenantId);
            cmd2.Parameters.AddWithValue("branchId", branchId);
            cmd2.Parameters.AddWithValue("idemp", idempotencyKey);

            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd2.ExecuteNonQueryAsync());
            Assert.Equal("23505", ex.SqlState); // unique_violation
            Assert.Equal("ix_catalog_availability_outbox_tenant_id_idempotency_key", ex.ConstraintName);
        }
    }

    [Fact]
    public async Task Outbox_UniqueConstraint_AllowsSameIdempotencyKey_ForDifferentTenants()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var sharedKey = $"shared-idemp-{Guid.NewGuid():N}";

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-u-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-u-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantA, branchA, "Branch A", $"ba-u-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantB, branchB, "Branch B", $"bb-u-{Guid.NewGuid():N}");

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        var sql = @"
            INSERT INTO tenancy.catalog_availability_outbox
                (id, tenant_id, branch_id, aggregate_id, event_type, payload, idempotency_key, occurred_at, created_at, status, attempt_count, max_attempts, schema_version)
            VALUES
                (@id, @tenantId, @branchId, 'agg-shared', 'CatalogItemQuick86', '{}', @idemp, NOW(), NOW(), 0, 0, 5, 1);";

        await using (var cmdA = new NpgsqlCommand(sql, conn))
        {
            cmdA.Parameters.AddWithValue("id", Guid.NewGuid());
            cmdA.Parameters.AddWithValue("tenantId", tenantA);
            cmdA.Parameters.AddWithValue("branchId", branchA);
            cmdA.Parameters.AddWithValue("idemp", sharedKey);
            await cmdA.ExecuteNonQueryAsync();
        }

        await using (var cmdB = new NpgsqlCommand(sql, conn))
        {
            cmdB.Parameters.AddWithValue("id", Guid.NewGuid());
            cmdB.Parameters.AddWithValue("tenantId", tenantB);
            cmdB.Parameters.AddWithValue("branchId", branchB);
            cmdB.Parameters.AddWithValue("idemp", sharedKey);
            await cmdB.ExecuteNonQueryAsync();
        }
    }
}
