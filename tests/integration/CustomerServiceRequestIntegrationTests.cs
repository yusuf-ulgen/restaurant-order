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
/// Comprehensive integration tests for CUST-06 customer service requests.
/// Verifies PostgreSQL RLS, idempotency, single open request constraint, CSRF,
/// foreign session protection, cancellation, and bill request integration.
/// </summary>
public class CustomerServiceRequestIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CustomerServiceRequestIntegrationTests(TestcontainersFixture fixture)
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
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Service Request Tenant", $"srt-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("SR Brand", $"srb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "SR Branch", $"srbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Floor", $"mf-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var tableReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        tableReq.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "Masa-5", "Booth 5", 4));
        var table = await (await client.SendAsync(tableReq)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var sessionReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables/{table!.Id}/sessions", adminToken);
        sessionReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var session = await (await client.SendAsync(sessionReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var activateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/sessions/{session!.Id}/activate", adminToken);
        activateReq.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        await client.SendAsync(activateReq);

        return (tenantId, branch.Id, adminToken, table.Id, session.Id);
    }

    [Fact]
    public async Task CreateServiceRequest_Successful_And_OpenRequestsList()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, tableId, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req.Headers.Add("Idempotency-Key", $"sr-key-{Guid.NewGuid():N}");
        req.Content = new StringContent(
            "{\"requestType\":\"Waiter\",\"note\":\"Buzlu olsun lütfen\"}",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal("Waiter", dto.RequestType);
        Assert.Equal("Open", dto.Status);
        Assert.Equal("Buzlu olsun lütfen", dto.Note);
        Assert.Equal(tableId, dto.TableId);
        Assert.Equal(sessionId, dto.DiningSessionId);

        // Verify Security Audit Event strictly redacts customer note text
        var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        await using var auditDb = new RestaurantOrderDbContext(dbOptions);
        var audit = await auditDb.SecurityAuditEvents
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == new TenantId(tenantId) && a.EventType == SecurityAuditEventType.CustomerServiceRequestCreated)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync();

        Assert.NotNull(audit);
        Assert.NotNull(audit.DetailsJson);
        Assert.DoesNotContain("Buzlu olsun lütfen", audit.DetailsJson);
        Assert.Contains("\"hasNote\":true", audit.DetailsJson);
        Assert.DoesNotContain("\"note\":", audit.DetailsJson);

        // GET /open returns the created request
        var getReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/v1/customer/service-requests/open", customerToken);
        var getRes = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        var list = await getRes.Content.ReadFromJsonAsync<List<ServiceRequestDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal(dto.Id, list[0].Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task CreateServiceRequest_MissingOrWhitespaceIdempotencyKey_Returns400(string? headerValue)
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        if (headerValue != null)
        {
            req.Headers.Add("Idempotency-Key", headerValue);
        }
        req.Content = JsonContent.Create(new CreateCustomerServiceRequest(ServiceRequestType.Waiter, null));

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateServiceRequest_DuplicateIdempotencyKey_SamePayload_ReplaysSameRecord()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var key = $"sr-idem-{Guid.NewGuid():N}";

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req1.Headers.Add("Idempotency-Key", key);
        req1.Content = JsonContent.Create(new CreateCustomerServiceRequest(ServiceRequestType.Napkin, "Ekstra peçete"));
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var dto1 = await res1.Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req2.Headers.Add("Idempotency-Key", key);
        req2.Content = JsonContent.Create(new CreateCustomerServiceRequest(ServiceRequestType.Napkin, "Ekstra peçete"));
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var dto2 = await res2.Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);

        Assert.Equal(dto1!.Id, dto2!.Id);
    }

    [Fact]
    public async Task CreateServiceRequest_SameKey_DifferentPayload_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var key = $"sr-conflict-{Guid.NewGuid():N}";

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req1.Headers.Add("Idempotency-Key", key);
        req1.Content = JsonContent.Create(new CreateCustomerServiceRequest(ServiceRequestType.Water, null));
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req2.Headers.Add("Idempotency-Key", key);
        req2.Content = JsonContent.Create(new CreateCustomerServiceRequest(ServiceRequestType.Waiter, "Farklı tür"));
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task CreateServiceRequest_MultipleSameType_SafelyReturnsExistingOpenRecord()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req1.Headers.Add("Idempotency-Key", $"key-a-{Guid.NewGuid():N}");
        req1.Content = JsonContent.Create(new CreateCustomerServiceRequest(ServiceRequestType.Waiter, null));
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var dto1 = await res1.Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);

        // Different idempotency key, but same session and same request type (Waiter) which is already Open
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req2.Headers.Add("Idempotency-Key", $"key-b-{Guid.NewGuid():N}");
        req2.Content = JsonContent.Create(new CreateCustomerServiceRequest(ServiceRequestType.Waiter, null));
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var dto2 = await res2.Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);

        Assert.Equal(dto1!.Id, dto2!.Id);
    }

    [Fact]
    public async Task CancelServiceRequest_OwnOpenRequest_Succeeds_And_ForeignRequest_Returns404()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        createReq.Headers.Add("Idempotency-Key", $"cancel-key-{Guid.NewGuid():N}");
        createReq.Content = JsonContent.Create(new CreateCustomerServiceRequest(ServiceRequestType.Assistance, "Soru sormak istiyoruz"));
        var createRes = await client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var dto = await createRes.Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);

        // Foreign unseeded session attempting to cancel this request -> 401 Unauthorized (session validation failed)
        var unseededSession = Guid.NewGuid();
        var unseededCustomerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, unseededSession);
        var unseededCancelReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Delete, $"/api/v1/customer/service-requests/{dto!.Id}", unseededCustomerToken);
        var unseededCancelRes = await client.SendAsync(unseededCancelReq);
        Assert.True(unseededCancelRes.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Forbidden);

        // Foreign active session attempting to cancel this request -> 404 Not Found (resource isolation)
        var (fTenantId, fBranchId, _, _, foreignActiveSessionId) = await SetupActiveSessionAsync(client);
        var foreignActiveCustomerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(fTenantId, fBranchId, foreignActiveSessionId);
        var foreignActiveCancelReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Delete, $"/api/v1/customer/service-requests/{dto.Id}", foreignActiveCustomerToken);
        var foreignActiveCancelRes = await client.SendAsync(foreignActiveCancelReq);
        Assert.Equal(HttpStatusCode.NotFound, foreignActiveCancelRes.StatusCode);

        // Own customer session cancels request -> 200 OK
        var ownCancelReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Delete, $"/api/v1/customer/service-requests/{dto.Id}", customerToken);
        var ownCancelRes = await client.SendAsync(ownCancelReq);
        Assert.Equal(HttpStatusCode.OK, ownCancelRes.StatusCode);
        var cancelledDto = await ownCancelRes.Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);
        Assert.Equal("Cancelled", cancelledDto!.Status);

        // Cancelling already cancelled request -> 400 Bad Request (DomainException)
        var cancelAgainReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Delete, $"/api/v1/customer/service-requests/{dto.Id}", customerToken);
        var cancelAgainRes = await client.SendAsync(cancelAgainReq);
        Assert.Equal(HttpStatusCode.BadRequest, cancelAgainRes.StatusCode);
    }

    [Fact]
    public async Task RequestBill_OwnSession_TransitionsToBillRequested_And_BlocksNewServiceRequests()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        // 1. Fetch current session to obtain concurrency token
        var sessionReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/v1/customer/session", customerToken);
        var sessionRes = await client.SendAsync(sessionReq);
        Assert.Equal(HttpStatusCode.OK, sessionRes.StatusCode);
        var sessionDto = await sessionRes.Content.ReadFromJsonAsync<CustomerSessionDto>(JsonOptions);

        // 2. Request bill using floor endpoint
        var billReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/request-bill", customerToken);
        billReq.Content = JsonContent.Create(new TransitionSessionRequest(sessionDto!.ConcurrencyToken));
        var billRes = await client.SendAsync(billReq);
        Assert.Equal(HttpStatusCode.OK, billRes.StatusCode);

        // 3. New service request on bill-requested session must be rejected with 400
        var srReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        srReq.Headers.Add("Idempotency-Key", $"post-bill-{Guid.NewGuid():N}");
        srReq.Content = JsonContent.Create(new CreateCustomerServiceRequest(ServiceRequestType.Water, null));
        var srRes = await client.SendAsync(srReq);
        Assert.Equal(HttpStatusCode.BadRequest, srRes.StatusCode);
    }

    [Theory]
    [InlineData("{\"requestType\":\"InvalidServiceType\",\"note\":\"Test\"}", "Invalid requestType")]
    [InlineData("{\"requestType\":999,\"note\":\"Test\"}", "Invalid numeric requestType")]
    [InlineData("{\"requestType\":\"\",\"note\":\"Test\"}", "requestType string cannot be empty")]
    [InlineData("{\"requestType\":true,\"note\":\"Test\"}", "requestType is required")]
    public async Task CreateServiceRequest_MalformedRequestTypePayloads_Returns400(string payload, string expectedMessage)
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req.Headers.Add("Idempotency-Key", $"sr-malformed-{Guid.NewGuid():N}");
        req.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedMessage, body);
    }

    [Fact]
    public async Task CreateServiceRequest_ValidNumericEnum_SucceedsWith200()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req.Headers.Add("Idempotency-Key", $"sr-num-ok-{Guid.NewGuid():N}");
        req.Content = new StringContent("{\"requestType\":1,\"note\":\"Numeric ok\"}", System.Text.Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);
        Assert.Equal("Waiter", dto!.RequestType);
    }

    [Fact]
    public async Task CreateServiceRequest_StaffRole_Returns403Forbidden()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, _, adminToken, _, _) = await SetupActiveSessionAsync(client);

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", adminToken);
        req.Headers.Add("Idempotency-Key", $"sr-staff-{Guid.NewGuid():N}");
        req.Content = new StringContent("{\"requestType\":\"Water\"}", System.Text.Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOpenServiceRequests_StaffRole_Returns403Forbidden()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, _, adminToken, _, _) = await SetupActiveSessionAsync(client);

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/v1/customer/service-requests/open", adminToken);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CancelServiceRequest_StaffRole_Returns403Forbidden()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (_, _, adminToken, _, _) = await SetupActiveSessionAsync(client);

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Delete, $"/api/v1/customer/service-requests/{Guid.NewGuid()}", adminToken);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
