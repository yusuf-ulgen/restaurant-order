using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public partial class CatalogAvailabilityOutboxIntegrationTests
{
    [Fact]
    public async Task Variant_Quick86_And_Restock_PersistOutboxMessages_Transactionally()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Var Outbox Tenant", $"vot-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Var Outbox Branch", $"vob-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // 1. Create Menu, Category, Item, and Variant
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("Var Menu", $"vm-{Guid.NewGuid():N}"));
        var menu = await (await client.SendAsync(menuReq)).Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("Var Cat", $"vc-{Guid.NewGuid():N}"));
        var cat = await (await client.SendAsync(catReq)).Content.ReadFromJsonAsync<MenuCategoryDto>();

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(cat!.Id, "Coffee", $"cf-{Guid.NewGuid():N}", 3000));
        var item = await (await client.SendAsync(itemReq)).Content.ReadFromJsonAsync<MenuItemDto>();

        var varReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item!.Id}/variants", adminToken);
        varReq.Content = JsonContent.Create(new CreateItemVariantCommand("Oat Milk", "OAT", 3500, 1, false));
        var variant = await (await client.SendAsync(varReq)).Content.ReadFromJsonAsync<ItemVariantDto>();

        // 2. Perform Quick86 on Variant
        var q86Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants/{variant!.Id}/quick-86", adminToken);
        q86Req.Content = JsonContent.Create(new Quick86VariantCommand("SoldOut", ConcurrencyToken: variant.ConcurrencyToken));
        var q86Resp = await client.SendAsync(q86Req);
        Assert.Equal(HttpStatusCode.OK, q86Resp.StatusCode);

        // Verify exactly 1 outbox message written for Variant Quick86
        await using (var db = CreateDbContext(tenantId))
        {
            var targetId = variant.Id.ToString();
            var outboxMessages = await db.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == targetId && m.EventType == "CatalogVariantQuick86")
                .ToListAsync();

            Assert.Single(outboxMessages);
            var msg = outboxMessages[0];
            Assert.Equal(tenantId, msg.TenantId.Value);
            Assert.Equal(branchId, msg.BranchId.Value);
            Assert.Equal(1, msg.SchemaVersion);
            Assert.Equal(CatalogOutboxStatus.Pending, msg.Status);
            Assert.Contains(variant.Id.ToString(), msg.Payload);
            Assert.Contains("SoldOut", msg.Payload);
        }

        // 3. Perform Restock on Variant
        var availReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/availability", adminToken);
        var availList = await (await client.SendAsync(availReq)).Content.ReadFromJsonAsync<List<BranchItemAvailabilityDto>>();
        var varAvail = availList!.Single(a => a.ItemVariantId == variant.Id);

        var restockReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/items/{item.Id}/variants/{variant.Id}/restock", adminToken);
        restockReq.Content = JsonContent.Create(new RestockVariantCommand("Restocked oat milk", varAvail.ConcurrencyToken));
        var restockResp = await client.SendAsync(restockReq);
        Assert.Equal(HttpStatusCode.OK, restockResp.StatusCode);

        // Verify restock outbox message for variant
        await using (var db = CreateDbContext(tenantId))
        {
            var targetId = variant.Id.ToString();
            var outboxMessages = await db.CatalogAvailabilityOutbox
                .Where(m => m.AggregateId == targetId && m.EventType == "CatalogVariantRestocked")
                .ToListAsync();

            Assert.Single(outboxMessages);
            var msg = outboxMessages[0];
            Assert.Equal(CatalogOutboxStatus.Pending, msg.Status);
            Assert.Contains("Restocked oat milk", msg.Payload);
        }
    }

    [Fact]
    public async Task TenantIsolation_Outbox_CannotBeAccessedByOtherTenant()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-out-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-out-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantA, branchA, "Branch A", $"ba-out-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantB, branchB, "Branch B", $"bb-out-{Guid.NewGuid():N}");

        // Insert outbox message directly under Tenant A
        var outboxA = CatalogAvailabilityOutboxMessage.Create(
            new TenantId(tenantA),
            new BranchId(branchA),
            "item-86-test",
            "CatalogItemQuick86",
            "{\"test\":true}",
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await using (var dbA = CreateDbContext(tenantA))
        {
            dbA.CatalogAvailabilityOutbox.Add(outboxA);
            await dbA.SaveChangesAsync();
        }

        // Query under Tenant B context - must return 0 messages (fail-closed tenant isolation)
        await using (var dbB = CreateDbContext(tenantB))
        {
            var messagesB = await dbB.CatalogAvailabilityOutbox.ToListAsync();
            Assert.DoesNotContain(messagesB, m => m.Id == outboxA.Id);
        }
    }
}
