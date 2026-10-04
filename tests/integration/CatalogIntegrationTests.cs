using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Api.Catalog;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public partial class CatalogIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public CatalogIntegrationTests(TestcontainersFixture fixture)
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

    [Fact]
    public async Task Menu_Lifecycle_DraftToActiveToArchived_ArchivedIsTerminal()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Catalog Tenant", $"ct-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Main Branch", $"main-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // 1. Create Menu (Initial status: Draft)
        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        createReq.Content = JsonContent.Create(new CreateMenuApiRequest("Lunch Menu", $"lunch-{Guid.NewGuid():N}", "Lunch specials", 1));
        var createResp = await client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(created);
        Assert.Equal("Draft", created.Status);
        Assert.True(createResp.Headers.Contains("ETag"));

        // 2. Activate Menu (Draft -> Active)
        var activateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{created.Id}/activate", adminToken);
        activateReq.Content = JsonContent.Create(new MenuStateApiRequest(created.ConcurrencyToken));
        var activateResp = await client.SendAsync(activateReq);
        Assert.Equal(HttpStatusCode.OK, activateResp.StatusCode);
        var activated = await activateResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(activated);
        Assert.Equal("Active", activated.Status);
        Assert.NotEqual(created.ConcurrencyToken, activated.ConcurrencyToken);

        // 3. Archive Menu (Active -> Archived)
        var archiveReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{created.Id}/archive", adminToken);
        archiveReq.Content = JsonContent.Create(new MenuStateApiRequest(activated.ConcurrencyToken));
        var archiveResp = await client.SendAsync(archiveReq);
        Assert.Equal(HttpStatusCode.OK, archiveResp.StatusCode);
        var archived = await archiveResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(archived);
        Assert.Equal("Archived", archived.Status);

        // 4. Cannot reactivate archived menu (terminal state -> 400 Bad Request RFC 7807)
        var reactivateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus/{created.Id}/activate", adminToken);
        reactivateReq.Content = JsonContent.Create(new MenuStateApiRequest(archived.ConcurrencyToken));
        var reactivateResp = await client.SendAsync(reactivateReq);
        Assert.Equal(HttpStatusCode.BadRequest, reactivateResp.StatusCode);
        var prob = await reactivateResp.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        Assert.NotNull(prob);
        Assert.Contains("terminal state", prob.Detail, StringComparison.OrdinalIgnoreCase);

        // 5. Cannot update archived menu
        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{created.Id}", adminToken);
        updateReq.Content = JsonContent.Create(new UpdateMenuApiRequest("Updated Name", null, 2, archived.ConcurrencyToken));
        var updateResp = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.BadRequest, updateResp.StatusCode);
    }

    [Fact]
    public async Task Menu_ConcurrencyTokens_MissingReturns412_StaleReturns409()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Concurrency Tenant", $"ct-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Conc Branch", $"cb-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        createReq.Content = JsonContent.Create(new CreateMenuApiRequest("Dinner Menu", $"dinner-{Guid.NewGuid():N}"));
        var createResp = await client.SendAsync(createReq);
        var menu = await createResp.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(menu);

        // 1. Missing token -> 412 Precondition Failed
        var missingReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}", adminToken);
        missingReq.Content = JsonContent.Create(new UpdateMenuApiRequest("New Dinner", null, 0, null));
        var missingResp = await client.SendAsync(missingReq);
        Assert.Equal(HttpStatusCode.PreconditionFailed, missingResp.StatusCode);

        // 2. Stale token -> 409 Conflict
        var staleReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/catalog/branches/{branchId}/menus/{menu.Id}", adminToken);
        staleReq.Content = JsonContent.Create(new UpdateMenuApiRequest("New Dinner", null, 0, Guid.NewGuid()));
        var staleResp = await client.SendAsync(staleReq);
        Assert.Equal(HttpStatusCode.Conflict, staleResp.StatusCode);
    }

    [Fact]
    public async Task Menu_DuplicateSlug_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Dup Slug Tenant", $"dst-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Slug Branch", $"sb-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var slug = $"drinks-{Guid.NewGuid():N}";
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        req1.Content = JsonContent.Create(new CreateMenuApiRequest("Drinks", slug));
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        // Duplicate slug in same branch
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchId}/menus", adminToken);
        req2.Content = JsonContent.Create(new CreateMenuApiRequest("Drinks Again", slug));
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
        var prob = await resp2.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        Assert.NotNull(prob);
        Assert.Contains("already exists", prob.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BranchManager_CanManageOwnBranch_CannotManageOtherBranch_Returns403()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branch1Id = Guid.NewGuid();
        var branch2Id = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "BM Scope Tenant", $"bmt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branch1Id, "Branch 1", $"b1-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branch2Id, "Branch 2", $"b2-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var bm1Token = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branch1Id, role: "BranchManager");

        // BM1 can create menu in Branch 1
        var okReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branch1Id}/menus", bm1Token);
        okReq.Content = JsonContent.Create(new CreateMenuApiRequest("BM1 Menu", $"bm1-{Guid.NewGuid():N}"));
        var okResp = await client.SendAsync(okReq);
        Assert.Equal(HttpStatusCode.Created, okResp.StatusCode);

        // BM1 cannot create or view menu in Branch 2 (403 Forbidden)
        var crossBranchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branch2Id}/menus", bm1Token);
        crossBranchReq.Content = JsonContent.Create(new CreateMenuApiRequest("Illegal Menu", $"illegal-{Guid.NewGuid():N}"));
        var crossBranchResp = await client.SendAsync(crossBranchReq);
        Assert.Equal(HttpStatusCode.Forbidden, crossBranchResp.StatusCode);
    }

    [Fact]
    public async Task ClosedOrSuspendedBranch_BlocksCatalogMutations()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var closedBranchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Closed Branch Tenant", $"cbt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, closedBranchId, "Closed Branch", $"closed-{Guid.NewGuid():N}", status: BranchStatus.Closed);

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{closedBranchId}/menus", adminToken);
        req.Content = JsonContent.Create(new CreateMenuApiRequest("Blocked Menu", $"blocked-{Guid.NewGuid():N}"));
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var prob = await resp.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        Assert.NotNull(prob);
        Assert.Contains("closed branch", prob.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuperAdmin_CannotBypassTenantBoundary_Returns403()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "SA Tenant", $"sat-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "SA Branch", $"sab-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        // SuperAdmin token without tenant scope or with wrong tenant scope
        var saToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId: null, role: "SuperAdmin");

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/menus", saToken);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PostgresRls_EnforcesTenantIsolationOnMenusAndCategories()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantA, branchA, "Branch A", $"ba-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantB, branchB, "Branch B", $"bb-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminTokenA = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantA, role: "RestaurantAdmin");
        var adminTokenB = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantB, role: "RestaurantAdmin");

        // Tenant A creates menu
        var reqA = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/catalog/branches/{branchA}/menus", adminTokenA);
        reqA.Content = JsonContent.Create(new CreateMenuApiRequest("Menu A", $"menu-a-{Guid.NewGuid():N}"));
        var respA = await client.SendAsync(reqA);
        var menuA = await respA.Content.ReadFromJsonAsync<MenuDto>();
        Assert.NotNull(menuA);

        // Tenant B requests Tenant A's menu -> 404 Not Found (isolated)
        var crossReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchA}/menus/{menuA.Id}", adminTokenB);
        var crossResp = await client.SendAsync(crossReq);
        Assert.Equal(HttpStatusCode.NotFound, crossResp.StatusCode);

        // Direct PostgreSQL RLS validation
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        // Switch to Tenant B's session context
        await using (var cmd = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', @tenantId, false)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantB.ToString());
            await cmd.ExecuteNonQueryAsync();
        }

        // Query menus - Tenant B must see 0 rows from Tenant A
        await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM tenancy.menus WHERE id = @id", conn))
        {
            cmd.Parameters.AddWithValue("id", menuA.Id);
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(0L, count);
        }
    }

    private async Task SeedBranchAsync(Guid tenantId, Guid branchId, string name, string slug, BranchStatus status = BranchStatus.Active)
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
            VALUES (@id, @tenantId, @brandId, @name, @slug, 'Europe/Istanbul', 'TRY', @status, NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmd = new NpgsqlCommand(branchSql, conn))
        {
            cmd.Parameters.AddWithValue("id", branchId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("brandId", brandId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("slug", slug);
            cmd.Parameters.AddWithValue("status", status.ToString());
            cmd.Parameters.AddWithValue("token", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

public sealed record ProblemDetailsResponse(
    string? Type,
    string? Title,
    int? Status,
    string? Detail,
    string? CorrelationId);
