using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class FloorTableIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FloorTableIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, Guid diningAreaId, string adminToken)> SetupFloorEnvironmentAsync(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Floor Test Tenant", $"ft-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Floor Brand", $"fb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        Assert.True(brandResp.IsSuccessStatusCode, $"POST /brands failed ({(int)brandResp.StatusCode})");
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Floor Branch", $"fbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branchResp = await client.SendAsync(branchReq);
        Assert.True(branchResp.IsSuccessStatusCode, $"POST /branches failed ({(int)branchResp.StatusCode})");
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Hall", $"mh-{Guid.NewGuid():N}", "Indoor", 0));
        var areaResp = await client.SendAsync(areaReq);
        Assert.True(areaResp.IsSuccessStatusCode, $"POST /dining-areas failed ({(int)areaResp.StatusCode})");
        var area = await areaResp.Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);
        Assert.NotNull(area);

        return (tenantId, branch.Id, area.Id, adminToken);
    }

    [Fact]
    public async Task Table_Create_And_Get_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, diningAreaId, adminToken) = await SetupFloorEnvironmentAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        createReq.Content = JsonContent.Create(new CreateTableApiRequest(
            diningAreaId, "T-10", "VIP Table 10", 4, 100, 150, 120, 80, 0, "Rectangle"));

        var createResp = await client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        Assert.True(createResp.Headers.Contains("ETag"));

        var created = await createResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("T-10", created.TableNumber);
        Assert.Equal("VIP Table 10", created.Name);
        Assert.Equal(4, created.Capacity);
        Assert.Equal(100, created.PositionX);
        Assert.Equal(150, created.PositionY);
        Assert.Equal(120, created.Width);
        Assert.Equal(80, created.Height);
        Assert.Equal(0, created.RotationDegrees);
        Assert.Equal("Rectangle", created.Shape);
        Assert.True(created.IsActive);
        Assert.Equal(1, created.QrVersion);

        // GET table by ID
        var getReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables/{created.Id}", adminToken);
        var getResp = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var fetched = await getResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
    }

    [Fact]
    public async Task Table_Update_WithValidConcurrencyToken_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, diningAreaId, adminToken) = await SetupFloorEnvironmentAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        createReq.Content = JsonContent.Create(new CreateTableApiRequest(
            diningAreaId, "T-20", "Table 20", 2, 50, 50, 80, 80, 0, "Square"));
        var createResp = await client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(created);

        // Update with body token
        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/floor/branches/{branchId}/tables/{created.Id}", adminToken);
        updateReq.Content = JsonContent.Create(new UpdateTableApiRequest(
            diningAreaId, "T-20-MOD", "Updated Table 20", 4, created.ConcurrencyToken));

        var updateResp = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        var updated = await updateResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("T-20-MOD", updated.TableNumber);
        Assert.Equal("Updated Table 20", updated.Name);
        Assert.Equal(4, updated.Capacity);
        Assert.NotEqual(created.ConcurrencyToken, updated.ConcurrencyToken);
    }

    [Fact]
    public async Task Table_Update_WithMissingPrecondition_Returns412()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, diningAreaId, adminToken) = await SetupFloorEnvironmentAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        createReq.Content = JsonContent.Create(new CreateTableApiRequest(
            diningAreaId, "T-30", "Table 30", 2));
        var createResp = await client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(created);

        // PUT without ConcurrencyToken in body or If-Match
        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/floor/branches/{branchId}/tables/{created.Id}", adminToken);
        updateReq.Content = JsonContent.Create(new UpdateTableApiRequest(
            diningAreaId, "T-30", "Table 30", 4, ConcurrencyToken: null));

        var updateResp = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.PreconditionFailed, updateResp.StatusCode);
    }

    [Fact]
    public async Task Table_Update_WithStaleConcurrencyToken_Returns409()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, diningAreaId, adminToken) = await SetupFloorEnvironmentAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        createReq.Content = JsonContent.Create(new CreateTableApiRequest(
            diningAreaId, "T-40", "Table 40", 2));
        var createResp = await client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(created);

        // PUT with wrong token
        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/floor/branches/{branchId}/tables/{created.Id}", adminToken);
        updateReq.Content = JsonContent.Create(new UpdateTableApiRequest(
            diningAreaId, "T-40", "Table 40", 4, ConcurrencyToken: Guid.NewGuid()));

        var updateResp = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.Conflict, updateResp.StatusCode);
    }

    [Fact]
    public async Task Table_UpdateLayout_And_ActivateDeactivate_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, diningAreaId, adminToken) = await SetupFloorEnvironmentAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        createReq.Content = JsonContent.Create(new CreateTableApiRequest(
            diningAreaId, "T-50", "Table 50", 4));
        var createResp = await client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(created);

        // Update layout
        var layoutReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/floor/branches/{branchId}/tables/{created.Id}/layout", adminToken);
        layoutReq.Content = JsonContent.Create(new UpdateTableLayoutApiRequest(
            300, 400, 150, 150, 45, "Round", created.ConcurrencyToken));
        var layoutResp = await client.SendAsync(layoutReq);
        Assert.Equal(HttpStatusCode.OK, layoutResp.StatusCode);
        var layoutUpdated = await layoutResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(layoutUpdated);
        Assert.Equal(300, layoutUpdated.PositionX);
        Assert.Equal(400, layoutUpdated.PositionY);
        Assert.Equal("Round", layoutUpdated.Shape);

        // Deactivate
        var deactReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{created.Id}/deactivate", adminToken);
        deactReq.Content = JsonContent.Create(new TableStateApiRequest(layoutUpdated.ConcurrencyToken));
        var deactResp = await client.SendAsync(deactReq);
        Assert.Equal(HttpStatusCode.OK, deactResp.StatusCode);
        var deactivated = await deactResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);

        // Reactivate
        var actReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{created.Id}/activate", adminToken);
        actReq.Content = JsonContent.Create(new TableStateApiRequest(deactivated.ConcurrencyToken));
        var actResp = await client.SendAsync(actReq);
        Assert.Equal(HttpStatusCode.OK, actResp.StatusCode);
        var activated = await actResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(activated);
        Assert.True(activated.IsActive);
    }

    [Fact]
    public async Task Table_DuplicateNumber_Returns409()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, diningAreaId, adminToken) = await SetupFloorEnvironmentAsync(client);

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        req1.Content = JsonContent.Create(new CreateTableApiRequest(
            diningAreaId, "T-DUP", "First Table", 4));
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        req2.Content = JsonContent.Create(new CreateTableApiRequest(
            diningAreaId, "T-DUP", "Second Table", 4));
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    [Fact]
    public async Task Table_BranchManager_CrossBranchAccess_Denied()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchAId, diningAreaId, _) = await SetupFloorEnvironmentAsync(client);

        var branchBId = Guid.NewGuid();
        var managerTokenBranchB = RestaurantConfigTestHelpers.GenerateToken(
            Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId: branchBId, role: "BranchManager");

        // BranchManager of Branch B tries to create table in Branch A
        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchAId}/tables", managerTokenBranchB);
        req.Content = JsonContent.Create(new CreateTableApiRequest(
            diningAreaId, "T-CROSS", "Cross Table", 4));

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Table_Audit_RollbackAtomicity_OnException()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;

        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var diningAreaId = DiningAreaId.New();

        await using var dbContext = new RestaurantOrderDbContext(options);
        var initialAuditCount = await dbContext.SecurityAuditEvents
            .CountAsync(e => e.TenantId == tenantId);

        var actor = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        var floorService = new FloorService(dbContext, new Microsoft.Extensions.Logging.Abstractions.NullLogger<FloorService>());

        // Attempting to create table in non-existent branch will fail validation before commit
        await Assert.ThrowsAnyAsync<Exception>(() => floorService.CreateTableAsync(
            tenantId,
            branchId,
            new CreateTableRequest(diningAreaId.Value, "T-FAIL", "Fail Table", 4),
            actor));

        // Verify zero orphaned audit logs were persisted
        var finalAuditCount = await dbContext.SecurityAuditEvents
            .CountAsync(e => e.TenantId == tenantId);
        Assert.Equal(initialAuditCount, finalAuditCount);
    }
}
