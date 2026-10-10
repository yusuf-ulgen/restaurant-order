using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Infrastructure.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class FloorQrAndBatchLayoutIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FloorQrAndBatchLayoutIntegrationTests(TestcontainersFixture fixture)
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
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Floor QR Tenant", $"fqr-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Floor QR Brand", $"fqb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        Assert.True(brandResp.IsSuccessStatusCode, $"POST /brands failed ({(int)brandResp.StatusCode})");
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Floor QR Branch", $"fqbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branchResp = await client.SendAsync(branchReq);
        Assert.True(branchResp.IsSuccessStatusCode, $"POST /branches failed ({(int)branchResp.StatusCode})");
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Garden", $"gd-{Guid.NewGuid():N}", "Outdoor", 0));
        var areaResp = await client.SendAsync(areaReq);
        Assert.True(areaResp.IsSuccessStatusCode, $"POST /dining-areas failed ({(int)areaResp.StatusCode})");
        var area = await areaResp.Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);
        Assert.NotNull(area);

        return (tenantId, branch.Id, area.Id, adminToken);
    }

    [Fact]
    public async Task Table_BatchUpdateLayout_AtomicallyUpdatesTables()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, diningAreaId, adminToken) = await SetupFloorEnvironmentAsync(client);

        // Create 2 tables
        var create1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        create1.Content = JsonContent.Create(new CreateTableApiRequest(diningAreaId, "B-01", "Batch Table 1", 2, 0, 0, 100, 100, 0, "Square"));
        var resp1 = await client.SendAsync(create1);
        var table1 = await resp1.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(table1);

        var create2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        create2.Content = JsonContent.Create(new CreateTableApiRequest(diningAreaId, "B-02", "Batch Table 2", 4, 100, 100, 120, 80, 0, "Rectangle"));
        var resp2 = await client.SendAsync(create2);
        var table2 = await resp2.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(table2);

        // Batch update layout
        var batchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Put, $"/api/v1/floor/branches/{branchId}/tables/batch-layout", adminToken);
        batchReq.Content = JsonContent.Create(new BatchUpdateTableLayoutApiRequest(new[]
        {
            new TableLayoutBatchApiItem(table1.Id, 250, 350, 110, 110, 45, "Round", table1.ConcurrencyToken),
            new TableLayoutBatchApiItem(table2.Id, 500, 600, 150, 90, 90, "Rectangle", table2.ConcurrencyToken)
        }));

        var batchResp = await client.SendAsync(batchReq);
        Assert.Equal(HttpStatusCode.OK, batchResp.StatusCode);

        var batchResult = await batchResp.Content.ReadFromJsonAsync<List<RestaurantTableDto>>(JsonOptions);
        Assert.NotNull(batchResult);
        Assert.Equal(2, batchResult.Count);

        var updated1 = batchResult.First(t => t.Id == table1.Id);
        Assert.Equal(250, updated1.PositionX);
        Assert.Equal(350, updated1.PositionY);
        Assert.Equal("Round", updated1.Shape);

        var updated2 = batchResult.First(t => t.Id == table2.Id);
        Assert.Equal(500, updated2.PositionX);
        Assert.Equal(600, updated2.PositionY);
        Assert.Equal(90, updated2.RotationDegrees);
    }

    [Fact]
    public async Task Table_List_FilterByAreaAndActive_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, diningAreaId, adminToken) = await SetupFloorEnvironmentAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        createReq.Content = JsonContent.Create(new CreateTableApiRequest(diningAreaId, "F-01", "Filtered Table", 4));
        var createResp = await client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        // Query with diningAreaId and isActive
        var queryReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables?diningAreaId={diningAreaId}&isActive=true", adminToken);
        var queryResp = await client.SendAsync(queryReq);
        Assert.Equal(HttpStatusCode.OK, queryResp.StatusCode);

        var tables = await queryResp.Content.ReadFromJsonAsync<List<RestaurantTableDto>>(JsonOptions);
        Assert.NotNull(tables);
        Assert.Contains(tables, t => t.TableNumber == "F-01");
    }

    [Fact]
    public async Task Table_QrRotate_And_Revoke_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, diningAreaId, adminToken) = await SetupFloorEnvironmentAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        createReq.Content = JsonContent.Create(new CreateTableApiRequest(diningAreaId, "QR-01", "QR Table", 4));
        var createResp = await client.SendAsync(createReq);
        var table = await createResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(table);
        Assert.Equal(1, table.QrVersion);

        // Rotate QR
        var rotateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{table.Id}/qr/rotate", adminToken);
        rotateReq.Content = JsonContent.Create(new RotateQrVersionRequest(table.ConcurrencyToken));
        var rotateResp = await client.SendAsync(rotateReq);
        Assert.Equal(HttpStatusCode.OK, rotateResp.StatusCode);

        var rotated = await rotateResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(rotated);
        Assert.Equal(2, rotated.QrVersion);

        // Revoke QR via another rotate
        var revokeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{table.Id}/qr/rotate", adminToken);
        revokeReq.Content = JsonContent.Create(new RotateQrVersionRequest(rotated.ConcurrencyToken));
        var revokeResp = await client.SendAsync(revokeReq);
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);

        var revoked = await revokeResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(revoked);
        Assert.Equal(3, revoked.QrVersion);
    }

    [Fact]
    public async Task Qr_Public_Resolve_Static_And_Exchange_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, diningAreaId, adminToken) = await SetupFloorEnvironmentAsync(client);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables", adminToken);
        createReq.Content = JsonContent.Create(new CreateTableApiRequest(diningAreaId, "PUB-01", "Public QR Table", 4));
        var createResp = await client.SendAsync(createReq);
        var table = await createResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(table);

        // Build valid static QR token using QrSecurityService
        var qrSecurity = new QrSecurityService(
            Microsoft.Extensions.Options.Options.Create(new QrSecurityOptions
            {
                CurrentKeyId = "k1",
                Keys = new Dictionary<string, string> { ["k1"] = "synthetic-dev-qr-key-32chars-minimum-entropy!!" }
            }),
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Development" });

        var payload = QrPayload.CreateStatic(
            "k1",
            new RestaurantOrder.Domain.Tenants.TenantId(tenantId),
            new RestaurantOrder.Domain.Branches.BranchId(branchId),
            table.PublicCode,
            table.QrVersion);

        var token = qrSecurity.GenerateToken(payload);

        // 1. Resolve QR token publicly
        var resolveResp = await client.GetAsync($"/api/v1/qr/resolve?token={token}");
        Assert.Equal(HttpStatusCode.OK, resolveResp.StatusCode);

        var resolveData = await resolveResp.Content.ReadFromJsonAsync<QrResolveResponse>(JsonOptions);
        Assert.NotNull(resolveData);
        Assert.Equal("PUB-01", resolveData.TableNumber);
        Assert.Equal("Floor QR Branch", resolveData.BranchName);
        Assert.Equal("static", resolveData.Mode);

        // 2. Exchange token for customer session
        var exchangeResp = await client.PostAsJsonAsync("/api/v1/qr/exchange", new QrExchangeRequest(token));
        Assert.Equal(HttpStatusCode.OK, exchangeResp.StatusCode);

        var exchangeData = await exchangeResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Open", exchangeData.GetProperty("sessionStatus").GetString());
        var sessionId = exchangeData.GetProperty("sessionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }

    [Fact]
    public async Task Qr_Public_Resolve_InvalidToken_Returns400()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var response = await client.GetAsync("/api/v1/qr/resolve?token=invalid.tampered.token");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
