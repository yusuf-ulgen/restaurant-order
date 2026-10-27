using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests for staff service request operations (Phase 10 Part 1/5).
/// Verifies PostgreSQL RLS multi-tenant branch isolation (NOSUPERUSER NOBYPASSRLS),
/// Acknowledge/Complete lifecycle, and customer-cancellation concurrency races.
/// </summary>
public class StaffServiceRequestEndpointsIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public StaffServiceRequestEndpointsIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, string adminToken, string waiterToken, Guid tableId, Guid sessionId)> SetupBranchAndSessionAsync(
        HttpClient client,
        string suffix)
    {
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, $"Staff Tenant {suffix}", $"st-{suffix}-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest($"Brand {suffix}", $"b-{suffix}-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, $"Branch {suffix}", $"br-{suffix}-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Area", $"MA-{suffix}", "Main area", 1));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>();

        var tableReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        tableReq.Content = JsonContent.Create(new CreateTableRequest(area!.Id, $"T-{suffix}", $"Table {suffix}", 4));
        var table = await (await client.SendAsync(tableReq)).Content.ReadFromJsonAsync<RestaurantTableDto>();

        var sessionReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables/{table!.Id}/sessions", adminToken);
        sessionReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var session = await (await client.SendAsync(sessionReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var activateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/sessions/{session!.Id}/activate", adminToken);
        activateReq.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        await client.SendAsync(activateReq);

        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(
            Guid.NewGuid(),
            Guid.NewGuid(),
            tenantId,
            branchId: branch.Id,
            role: "Waiter");

        return (tenantId, branch.Id, adminToken, waiterToken, table.Id, session.Id);
    }

    [Fact]
    public async Task StaffServiceRequests_List_EnforcesTwoTenantAndTwoBranchIsolation()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture))
        {
            return;
        }

        await EnsureMigrationsAppliedAsync();
        using var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);

        // Branch 1 in Tenant 1
        var (t1, b1, _, waiter1Token, table1Id, session1Id) = await SetupBranchAndSessionAsync(client, "iso-1");
        // Branch 2 in Tenant 2
        var (t2, b2, _, waiter2Token, table2Id, session2Id) = await SetupBranchAndSessionAsync(client, "iso-2");

        // Customer creates request in Tenant 1
        var cust1Token = RestaurantConfigTestHelpers.GenerateCustomerToken(t1, b1, session1Id);
        var custReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/service-requests", cust1Token);
        custReq.Headers.Add("Idempotency-Key", $"key-iso-{Guid.NewGuid():N}");
        custReq.Content = new StringContent("{\"requestType\":\"Water\",\"note\":\"Cold please\"}", System.Text.Encoding.UTF8, "application/json");
        var custResp = await client.SendAsync(custReq);
        Assert.Equal(HttpStatusCode.OK, custResp.StatusCode);

        // Waiter 1 lists service requests -> should see 1 item
        var list1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/floor/branches/{b1}/service-requests", waiter1Token);
        var list1Resp = await client.SendAsync(list1Req);
        Assert.Equal(HttpStatusCode.OK, list1Resp.StatusCode);
        var items1 = await list1Resp.Content.ReadFromJsonAsync<IReadOnlyList<StaffServiceRequestDto>>(JsonOptions);
        Assert.NotNull(items1);
        Assert.Single(items1);

        // Waiter 2 from Tenant 2 lists service requests on Branch 2 -> should see 0 items
        var list2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/floor/branches/{b2}/service-requests", waiter2Token);
        var list2Resp = await client.SendAsync(list2Req);
        Assert.Equal(HttpStatusCode.OK, list2Resp.StatusCode);
        var items2 = await list2Resp.Content.ReadFromJsonAsync<IReadOnlyList<StaffServiceRequestDto>>(JsonOptions);
        Assert.NotNull(items2);
        Assert.Empty(items2);

        // Waiter 2 attempts to query Branch 1 -> 403 Forbidden (cross-tenant & cross-branch blocked)
        var crossReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/floor/branches/{b1}/service-requests", waiter2Token);
        var crossResp = await client.SendAsync(crossReq);
        Assert.Equal(HttpStatusCode.Forbidden, crossResp.StatusCode);
    }

    [Fact]
    public async Task StaffServiceRequests_AcknowledgeAndComplete_TransitionsStatusAndReturnsETag()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture))
        {
            return;
        }

        await EnsureMigrationsAppliedAsync();
        using var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);

        var (t, b, _, waiterToken, tableId, sessionId) = await SetupBranchAndSessionAsync(client, "ack-comp");

        // Customer creates request
        var custToken = RestaurantConfigTestHelpers.GenerateCustomerToken(t, b, sessionId);
        var custReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/service-requests", custToken);
        custReq.Headers.Add("Idempotency-Key", $"key-ac-{Guid.NewGuid():N}");
        custReq.Content = new StringContent("{\"requestType\":\"Waiter\",\"note\":\"Check\"}", System.Text.Encoding.UTF8, "application/json");
        var custResp = await client.SendAsync(custReq);
        Assert.Equal(HttpStatusCode.OK, custResp.StatusCode);
        var created = await custResp.Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);
        Assert.NotNull(created);

        // Waiter acknowledges request
        var ackReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{b}/service-requests/{created.Id}/acknowledge", waiterToken);
        ackReq.Headers.IfMatch.ParseAdd($"\"{created.ConcurrencyToken:D}\"");
        ackReq.Content = new StringContent($"{{\"concurrencyToken\":\"{created.ConcurrencyToken:D}\"}}", System.Text.Encoding.UTF8, "application/json");
        var ackResp = await client.SendAsync(ackReq);
        Assert.Equal(HttpStatusCode.OK, ackResp.StatusCode);
        var acknowledged = await ackResp.Content.ReadFromJsonAsync<StaffServiceRequestDto>(JsonOptions);
        Assert.NotNull(acknowledged);
        Assert.Equal("Acknowledged", acknowledged.Status);
        Assert.NotEqual(created.ConcurrencyToken, acknowledged.ConcurrencyToken);

        // Waiter completes request
        var compReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{b}/service-requests/{created.Id}/complete", waiterToken);
        compReq.Headers.IfMatch.ParseAdd($"\"{acknowledged.ConcurrencyToken:D}\"");
        compReq.Content = new StringContent($"{{\"concurrencyToken\":\"{acknowledged.ConcurrencyToken:D}\"}}", System.Text.Encoding.UTF8, "application/json");
        var compResp = await client.SendAsync(compReq);
        Assert.Equal(HttpStatusCode.OK, compResp.StatusCode);
        var completed = await compResp.Content.ReadFromJsonAsync<StaffServiceRequestDto>(JsonOptions);
        Assert.NotNull(completed);
        Assert.Equal("Completed", completed.Status);
    }

    [Fact]
    public async Task StaffServiceRequests_CustomerCancelRace_ReturnsConflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture))
        {
            return;
        }

        await EnsureMigrationsAppliedAsync();
        using var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);

        var (t, b, _, waiterToken, tableId, sessionId) = await SetupBranchAndSessionAsync(client, "cancel-race");

        // Customer creates request
        var custToken = RestaurantConfigTestHelpers.GenerateCustomerToken(t, b, sessionId);
        var custReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/service-requests", custToken);
        custReq.Headers.Add("Idempotency-Key", $"key-race-{Guid.NewGuid():N}");
        custReq.Content = new StringContent("{\"requestType\":\"Assistance\",\"note\":null}", System.Text.Encoding.UTF8, "application/json");
        var custResp = await client.SendAsync(custReq);
        Assert.Equal(HttpStatusCode.OK, custResp.StatusCode);
        var created = await custResp.Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);
        Assert.NotNull(created);

        // Customer cancels request via contract: DELETE /api/v1/customer/service-requests/{id}
        var cancelReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/v1/customer/service-requests/{created.Id}", custToken);
        cancelReq.Headers.IfMatch.ParseAdd($"\"{created.ConcurrencyToken:D}\"");
        var cancelResp = await client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        // Waiter attempts to acknowledge with the old ConcurrencyToken -> 409 Conflict
        var ackReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{b}/service-requests/{created.Id}/acknowledge", waiterToken);
        ackReq.Headers.IfMatch.ParseAdd($"\"{created.ConcurrencyToken:D}\"");
        ackReq.Content = new StringContent($"{{\"concurrencyToken\":\"{created.ConcurrencyToken:D}\"}}", System.Text.Encoding.UTF8, "application/json");
        var ackResp = await client.SendAsync(ackReq);
        Assert.Equal(HttpStatusCode.Conflict, ackResp.StatusCode);
    }
}
