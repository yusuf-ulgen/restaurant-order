using System.Net;
using System.Net.Http.Json;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Catalog;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public partial class CatalogIntegrationTests
{
    [Fact]
    public async Task Category_CrudAndLifecycle_WorksAsExpected()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Cat Crud Tenant", $"cct-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Main Branch", $"main-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // 1. Create Menu
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuApiRequest("Main Menu", $"main-{Guid.NewGuid():N}"));
        var menuResp = await client.SendAsync(menuReq);
        var menu = await menuResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(menu);

        // 2. Create Category
        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryApiRequest("Starters", $"starters-{Guid.NewGuid():N}", "Delicious starters", 1));
        var catResp = await client.SendAsync(catReq);
        Assert.Equal(HttpStatusCode.Created, catResp.StatusCode);
        var category = await catResp.Content.ReadFromJsonAsync<MenuCategoryDto>();
        Assert.NotNull(category);
        Assert.True(category.IsActive);
        Assert.Equal("Starters", category.Name);
        Assert.True(catResp.Headers.Contains("ETag"));

        // 3. Get Category
        var getReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories/{category.Id}", adminToken);
        var getResp = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var fetched = await getResp.Content.ReadFromJsonAsync<MenuCategoryDto>();
        Assert.NotNull(fetched);
        Assert.Equal(category.Id, fetched.Id);

        // 4. Update Category
        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories/{category.Id}", adminToken);
        updateReq.Content = JsonContent.Create(new UpdateMenuCategoryApiRequest("Cold Starters", "Cold mezze only", 2, category.ConcurrencyToken));
        var updateResp = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<MenuCategoryDto>();
        Assert.NotNull(updated);
        Assert.Equal("Cold Starters", updated.Name);
        Assert.Equal(2, updated.SortOrder);
        Assert.NotEqual(category.ConcurrencyToken, updated.ConcurrencyToken);

        // 5. Deactivate Category
        var deactReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories/{category.Id}/deactivate", adminToken);
        deactReq.Content = JsonContent.Create(new MenuCategoryStateApiRequest(updated.ConcurrencyToken));
        var deactResp = await client.SendAsync(deactReq);
        Assert.Equal(HttpStatusCode.OK, deactResp.StatusCode);
        var deactivated = await deactResp.Content.ReadFromJsonAsync<MenuCategoryDto>();
        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);

        // 6. Activate Category
        var actReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories/{category.Id}/activate", adminToken);
        actReq.Content = JsonContent.Create(new MenuCategoryStateApiRequest(deactivated.ConcurrencyToken));
        var actResp = await client.SendAsync(actReq);
        Assert.Equal(HttpStatusCode.OK, actResp.StatusCode);
        var activated = await actResp.Content.ReadFromJsonAsync<MenuCategoryDto>();
        Assert.NotNull(activated);
        Assert.True(activated.IsActive);
    }

    [Fact]
    public async Task Category_DuplicateSlug_InSameMenu_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Cat Dup Slug Tenant", $"cdst-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Main Branch", $"main-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuApiRequest("Dup Cat Menu", $"menu-{Guid.NewGuid():N}"));
        var menuResp = await client.SendAsync(menuReq);
        var menu = await menuResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(menu);

        var slug = $"mains-{Guid.NewGuid():N}";
        var cat1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories", adminToken);
        cat1Req.Content = JsonContent.Create(new CreateMenuCategoryApiRequest("Mains", slug));
        var cat1Resp = await client.SendAsync(cat1Req);
        Assert.Equal(HttpStatusCode.Created, cat1Resp.StatusCode);

        // Duplicate slug in same menu
        var cat2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories", adminToken);
        cat2Req.Content = JsonContent.Create(new CreateMenuCategoryApiRequest("Mains Duplicate", slug));
        var cat2Resp = await client.SendAsync(cat2Req);
        Assert.Equal(HttpStatusCode.Conflict, cat2Resp.StatusCode);
    }

    [Fact]
    public async Task Category_CannotMutate_OnArchivedMenu()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Archived Menu Tenant", $"amt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Main Branch", $"main-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // 1. Create Menu and Archive it
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuApiRequest("To Be Archived", $"arch-{Guid.NewGuid():N}"));
        var menuResp = await client.SendAsync(menuReq);
        var menu = await menuResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(menu);

        var archiveReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/archive", adminToken);
        archiveReq.Content = JsonContent.Create(new MenuStateApiRequest(menu.ConcurrencyToken));
        var archiveResp = await client.SendAsync(archiveReq);
        Assert.Equal(HttpStatusCode.OK, archiveResp.StatusCode);

        // 2. Attempt to create category on archived menu -> 400 Bad Request
        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryApiRequest("Blocked Cat", $"blocked-{Guid.NewGuid():N}"));
        var catResp = await client.SendAsync(catReq);
        Assert.Equal(HttpStatusCode.BadRequest, catResp.StatusCode);
        var prob = await catResp.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        Assert.NotNull(prob);
        Assert.Contains("terminal state", prob.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Category_Reorder_Atomic_RollbackOnStaleToken()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Reorder Tenant", $"rt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Main Branch", $"main-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuApiRequest("Reorder Menu", $"rm-{Guid.NewGuid():N}"));
        var menuResp = await client.SendAsync(menuReq);
        var menu = await menuResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(menu);

        // Create 2 categories
        var cat1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories", adminToken);
        cat1Req.Content = JsonContent.Create(new CreateMenuCategoryApiRequest("Cat 1", $"cat1-{Guid.NewGuid():N}", null, 0));
        var cat1Resp = await client.SendAsync(cat1Req);
        var cat1 = await cat1Resp.Content.ReadFromJsonAsync<MenuCategoryDto>();
        Assert.NotNull(cat1);

        var cat2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories", adminToken);
        cat2Req.Content = JsonContent.Create(new CreateMenuCategoryApiRequest("Cat 2", $"cat2-{Guid.NewGuid():N}", null, 1));
        var cat2Resp = await client.SendAsync(cat2Req);
        var cat2 = await cat2Resp.Content.ReadFromJsonAsync<MenuCategoryDto>();
        Assert.NotNull(cat2);

        // 1. Reorder with stale token on Cat 2 -> 409 Conflict
        var staleReorderReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories/reorder", adminToken);
        staleReorderReq.Content = JsonContent.Create(new ReorderCategoriesApiRequest(
        [
            new CategoryReorderItemApiRequest(cat1.Id, 1, cat1.ConcurrencyToken),
            new CategoryReorderItemApiRequest(cat2.Id, 0, Guid.NewGuid()) // Stale token!
        ]));
        var staleResp = await client.SendAsync(staleReorderReq);
        Assert.Equal(HttpStatusCode.Conflict, staleResp.StatusCode);

        // Verify neither category was modified (rollback verified)
        var listReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories", adminToken);
        var listResp = await client.SendAsync(listReq);
        var list = await listResp.Content.ReadFromJsonAsync<List<MenuCategoryDto>>();
        Assert.NotNull(list);
        var c1 = list.Single(c => c.Id == cat1.Id);
        var c2 = list.Single(c => c.Id == cat2.Id);
        Assert.Equal(0, c1.SortOrder);
        Assert.Equal(1, c2.SortOrder);

        // 2. Successful Reorder with valid tokens
        var validReorderReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}/categories/reorder", adminToken);
        validReorderReq.Content = JsonContent.Create(new ReorderCategoriesApiRequest(
        [
            new CategoryReorderItemApiRequest(cat1.Id, 10, cat1.ConcurrencyToken),
            new CategoryReorderItemApiRequest(cat2.Id, 20, cat2.ConcurrencyToken)
        ]));
        var validResp = await client.SendAsync(validReorderReq);
        Assert.Equal(HttpStatusCode.OK, validResp.StatusCode);
        var reordered = await validResp.Content.ReadFromJsonAsync<List<MenuCategoryDto>>();
        Assert.NotNull(reordered);
        var reorderedC1 = reordered.Single(c => c.Id == cat1.Id);
        var reorderedC2 = reordered.Single(c => c.Id == cat2.Id);
        Assert.Equal(10, reorderedC1.SortOrder);
        Assert.Equal(20, reorderedC2.SortOrder);
    }
}
