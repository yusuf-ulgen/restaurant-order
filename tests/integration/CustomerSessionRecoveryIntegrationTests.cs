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

/// <summary>
/// Integration tests verifying the GET /api/v1/customer/session recovery endpoint.
/// Tests ensure verified claims, PostgreSQL source of truth active validation,
/// role restriction (Customer only), safe metadata without token leakage, and RFC 7807 error responses.
/// </summary>
public class CustomerSessionRecoveryIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CustomerSessionRecoveryIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, string adminToken, Guid tableId, Guid sessionId)> SetupActiveSessionAsync(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Session Recovery Tenant", $"srt-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Recovery Brand", $"rb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "Recovery Branch", $"rbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Hall", $"mh-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var tableReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        tableReq.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "T-10", "Window Table", 4));
        var table = await (await client.SendAsync(tableReq)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var sessionReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables/{table!.Id}/sessions", adminToken);
        sessionReq.Content = JsonContent.Create(new OpenDiningSessionRequest(3));
        var session = await (await client.SendAsync(sessionReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var activateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/sessions/{session!.Id}/activate", adminToken);
        activateReq.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        var actResp = await client.SendAsync(activateReq);
        Assert.Equal(HttpStatusCode.OK, actResp.StatusCode);

        return (tenantId, branch.Id, adminToken, table.Id, session.Id);
    }

    [Fact]
    public async Task GetCustomerSession_WithValidCustomerTokenAndActiveSession_Returns200OkWithSafeMetadata()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, tableId, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var request = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/v1/customer/session", customerToken);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", rawJson, StringComparison.OrdinalIgnoreCase);

        var sessionDto = JsonSerializer.Deserialize<CustomerSessionDto>(rawJson, JsonOptions);
        Assert.NotNull(sessionDto);
        Assert.Equal(sessionId, sessionDto.SessionId);
        Assert.Equal(tenantId, sessionDto.TenantId);
        Assert.Equal(branchId, sessionDto.BranchId);
        Assert.Equal(tableId, sessionDto.TableId);
        Assert.Equal("T-10", sessionDto.TableNumber);
        Assert.Equal("Window Table", sessionDto.TableName);
        Assert.Equal("Active", sessionDto.SessionStatus);
        Assert.Equal(3, sessionDto.GuestCount);
        Assert.NotEqual(Guid.Empty, sessionDto.ConcurrencyToken);

        var etag = response.Headers.ETag;
        Assert.NotNull(etag);
        Assert.Equal($"\"{sessionDto.ConcurrencyToken}\"", etag.Tag);
    }

    [Fact]
    public async Task GetCustomerSession_WithoutToken_Returns401Unauthorized()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customer/session");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomerSession_WithStaffRoleToken_Returns403Forbidden()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        // Staff token with Waiter role attempting to access customer endpoint
        var staffToken = RestaurantConfigTestHelpers.GenerateToken(
            Guid.NewGuid(), sessionId, tenantId, branchId, role: "Waiter");

        var request = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/v1/customer/session", staffToken);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomerSession_WhenSessionIsClosed_Returns401SessionExpired()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, adminToken, _, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        // Close the session via staff endpoint
        var sessionGetReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}", adminToken);
        var sessionDto = await (await client.SendAsync(sessionGetReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var billReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/request-bill", adminToken);
        billReq.Headers.Add("If-Match", $"\"{sessionDto!.ConcurrencyToken}\"");
        var billResp = await client.SendAsync(billReq);
        Assert.Equal(HttpStatusCode.OK, billResp.StatusCode);
        var billSession = await billResp.Content.ReadFromJsonAsync<DiningSessionDto>();

        var closeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/close", adminToken);
        closeReq.Headers.Add("If-Match", $"\"{billSession!.ConcurrencyToken}\"");
        closeReq.Content = JsonContent.Create(new CloseDiningSessionRequest("Customer left", billSession.ConcurrencyToken));
        var closeResp = await client.SendAsync(closeReq);
        Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);

        // Now call customer session recovery
        var recoveryReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/v1/customer/session", customerToken);
        var recoveryResp = await client.SendAsync(recoveryReq);

        Assert.Equal(HttpStatusCode.Unauthorized, recoveryResp.StatusCode);
        var errorBody = await recoveryResp.Content.ReadAsStringAsync();
        Assert.Contains("Session Expired", errorBody);
    }

    [Fact]
    public async Task GetCustomerSession_WithForeignTenantOrBranchClaims_Returns401Or404()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        var foreignTenantId = Guid.NewGuid();
        var forgedToken = RestaurantConfigTestHelpers.GenerateCustomerToken(foreignTenantId, branchId, sessionId);

        var request = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/v1/customer/session", forgedToken);

        var response = await client.SendAsync(request);
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 401 or 404 for forged tenant session token, got {response.StatusCode}");
    }
}
