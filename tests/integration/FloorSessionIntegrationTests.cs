using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class FloorSessionIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FloorSessionIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, Guid tableId, string adminToken)> SetupTableEnvironmentAsync(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Session Test Tenant", $"st-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Session Brand", $"sb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        Assert.True(brandResp.IsSuccessStatusCode);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "Session Branch", $"sbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branchResp = await client.SendAsync(branchReq);
        Assert.True(branchResp.IsSuccessStatusCode);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Hall", $"smh-{Guid.NewGuid():N}", "Indoor", 0));
        var areaResp = await client.SendAsync(areaReq);
        Assert.True(areaResp.IsSuccessStatusCode);
        var area = await areaResp.Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);
        Assert.NotNull(area);

        var tableReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        tableReq.Content = JsonContent.Create(new CreateTableApiRequest(
            area.Id, "T-01", "Table 1", 4, 100, 100, 100, 100, 0, "Square"));
        var tableResp = await client.SendAsync(tableReq);
        Assert.True(tableResp.IsSuccessStatusCode);
        var table = await tableResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(table);

        return (tenantId, branch.Id, table.Id, adminToken);
    }

    [Fact]
    public async Task Session_Open_Activate_RequestBill_Close_FullLifecycle_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, tableId, adminToken) = await SetupTableEnvironmentAsync(client);

        // 1. Open session (Open status, 201 Created)
        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/sessions", adminToken);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));

        var openResp = await client.SendAsync(openReq);
        Assert.Equal(HttpStatusCode.Created, openResp.StatusCode);
        Assert.True(openResp.Headers.Contains("ETag"));
        var openSession = await openResp.Content.ReadFromJsonAsync<DiningSessionDto>(JsonOptions);
        Assert.NotNull(openSession);
        Assert.Equal("Open", openSession.Status);
        Assert.Equal(2, openSession.GuestCount);

        var token1 = openResp.Headers.ETag!.Tag.Trim('"');

        // 2. Activate session (Open -> Active)
        var activateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{openSession.Id}/activate", adminToken);
        activateReq.Headers.IfMatch.ParseAdd($"\"{token1}\"");

        var activateResp = await client.SendAsync(activateReq);
        Assert.Equal(HttpStatusCode.OK, activateResp.StatusCode);
        var activeSession = await activateResp.Content.ReadFromJsonAsync<DiningSessionDto>(JsonOptions);
        Assert.NotNull(activeSession);
        Assert.Equal("Active", activeSession.Status);
        Assert.NotNull(activeSession.ActivatedAtUtc);

        var token2 = activateResp.Headers.ETag!.Tag.Trim('"');

        // 3. Request Bill (Active -> BillRequested)
        var billReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{openSession.Id}/request-bill", adminToken);
        billReq.Headers.IfMatch.ParseAdd($"\"{token2}\"");

        var billResp = await client.SendAsync(billReq);
        Assert.Equal(HttpStatusCode.OK, billResp.StatusCode);
        var billSession = await billResp.Content.ReadFromJsonAsync<DiningSessionDto>(JsonOptions);
        Assert.NotNull(billSession);
        Assert.Equal("BillRequested", billSession.Status);
        Assert.NotNull(billSession.BillRequestedAtUtc);

        var token3 = billResp.Headers.ETag!.Tag.Trim('"');

        // 4. Close session (BillRequested -> Closed)
        var closeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{openSession.Id}/close", adminToken);
        closeReq.Headers.IfMatch.ParseAdd($"\"{token3}\"");
        closeReq.Content = JsonContent.Create(new CloseDiningSessionRequest("Guest paid in cash"));

        var closeResp = await client.SendAsync(closeReq);
        Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);
        var closedSession = await closeResp.Content.ReadFromJsonAsync<DiningSessionDto>(JsonOptions);
        Assert.NotNull(closedSession);
        Assert.Equal("Closed", closedSession.Status);
        Assert.Equal("Guest paid in cash", closedSession.CloseReason);
        Assert.NotNull(closedSession.ClosedAtUtc);

        // 5. Verify table active session is now null
        var checkReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/session", adminToken);
        var checkResp = await client.SendAsync(checkReq);
        Assert.Equal(HttpStatusCode.NotFound, checkResp.StatusCode);

        // 6. Verify session history contains closed session
        var historyReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/sessions", adminToken);
        var historyResp = await client.SendAsync(historyReq);
        Assert.Equal(HttpStatusCode.OK, historyResp.StatusCode);
        var history = await historyResp.Content.ReadFromJsonAsync<List<DiningSessionDto>>(JsonOptions);
        Assert.NotNull(history);
        Assert.Single(history);
        Assert.Equal(closedSession.Id, history[0].Id);
    }

    [Fact]
    public async Task Session_ConcurrentOpening_PartialUniqueIndex_ReturnsConflict409()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, tableId, adminToken) = await SetupTableEnvironmentAsync(client);

        // Open first session
        var open1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/sessions", adminToken);
        open1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var resp1 = await client.SendAsync(open1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        // Try to open second session on the same table while first is open
        var open2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/sessions", adminToken);
        open2.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var resp2 = await client.SendAsync(open2);

        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    [Fact]
    public async Task Session_Transition_MissingIfMatch_ReturnsPreconditionFailed412()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, tableId, adminToken) = await SetupTableEnvironmentAsync(client);

        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/sessions", adminToken);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var openResp = await client.SendAsync(openReq);
        var session = await openResp.Content.ReadFromJsonAsync<DiningSessionDto>(JsonOptions);
        Assert.NotNull(session);

        // Missing If-Match header and missing body token
        var actReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/activate", adminToken);
        var actResp = await client.SendAsync(actReq);

        Assert.Equal(HttpStatusCode.PreconditionFailed, actResp.StatusCode);
    }

    [Fact]
    public async Task Session_Transition_StaleToken_ReturnsConflict409()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, tableId, adminToken) = await SetupTableEnvironmentAsync(client);

        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/sessions", adminToken);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var openResp = await client.SendAsync(openReq);
        var session = await openResp.Content.ReadFromJsonAsync<DiningSessionDto>(JsonOptions);
        Assert.NotNull(session);

        // Random/stale token in If-Match
        var staleToken = Guid.NewGuid();
        var actReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/activate", adminToken);
        actReq.Headers.IfMatch.ParseAdd($"\"{staleToken}\"");
        var actResp = await client.SendAsync(actReq);

        Assert.Equal(HttpStatusCode.Conflict, actResp.StatusCode);
    }

    [Fact]
    public async Task Table_Deactivate_BlockedWhenDiningSessionActive()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, branchId, tableId, adminToken) = await SetupTableEnvironmentAsync(client);

        // Open session
        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/sessions", adminToken);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var openResp = await client.SendAsync(openReq);
        Assert.Equal(HttpStatusCode.Created, openResp.StatusCode);

        // Attempt to deactivate table
        var tableGetReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables/{tableId}", adminToken);
        var tableGetResp = await client.SendAsync(tableGetReq);
        var tableDto = await tableGetResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        Assert.NotNull(tableDto);

        var deactReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/deactivate", adminToken);
        deactReq.Headers.IfMatch.ParseAdd($"\"{tableDto.ConcurrencyToken}\"");
        var deactResp = await client.SendAsync(deactReq);

        Assert.Equal(HttpStatusCode.BadRequest, deactResp.StatusCode);
    }
}
