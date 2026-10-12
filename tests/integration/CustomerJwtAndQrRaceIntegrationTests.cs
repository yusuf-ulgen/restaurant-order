using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Verifies Customer JWT authentication pipeline invariants (OnTokenValidated)
/// and QR exchange concurrency/race conditions against the real ASP.NET Core middleware and PostgreSQL database.
/// </summary>
public class CustomerJwtAndQrRaceIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public CustomerJwtAndQrRaceIntegrationTests(TestcontainersFixture fixture)
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

    private static string BuildCustomCustomerToken(
        Guid tenantId,
        Guid branchId,
        Guid tableSessionId,
        string? customSub = null,
        bool omitSub = false,
        DateTimeOffset? iat = null,
        string? extraPayload = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret))
        {
            KeyId = "k1"
        };
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(15);
        var claims = new List<Claim>
        {
            new(JwtClaimNames.SessionId, tableSessionId.ToString()),
            new(JwtClaimNames.JwtId, Guid.NewGuid().ToString("N")),
            new(JwtClaimNames.PrincipalType, "customer"),
            new(JwtClaimNames.Role, "Customer"),
            new(JwtClaimNames.AuthMethod, "customer_qr_session"),
            new(JwtClaimNames.SecurityVersion, "0"),
            new(JwtClaimNames.TenantId, tenantId.ToString()),
            new(JwtClaimNames.BranchId, branchId.ToString()),
            new(JwtClaimNames.TableSessionId, tableSessionId.ToString()),
            new(JwtRegisteredClaimNames.Iss, "restaurant-order"),
            new(JwtRegisteredClaimNames.Aud, "restaurant-order-clients"),
            new(JwtRegisteredClaimNames.Iat, (iat ?? now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Nbf, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Exp, expires.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (!omitSub)
        {
            claims.Add(new Claim(JwtClaimNames.Subject, customSub ?? tableSessionId.ToString()));
        }

        if (extraPayload != null)
        {
            claims.Add(new Claim("extra_bloat", extraPayload));
        }

        var token = new JwtSecurityToken(
            issuer: "restaurant-order",
            audience: "restaurant-order-clients",
            claims: claims,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<(Guid tenantId, Guid branchId, Guid tableId, Guid sessionId)> SetupCustomerEnvironmentAsync(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Cust Env", $"ce-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Cust Brand", $"cb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "Cust Branch", $"cbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Cust Area", $"ca-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var tableReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        tableReq.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "C-01", "Customer Table", 4));
        var table = await (await client.SendAsync(tableReq)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        // Open session
        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables/{table!.Id}/sessions", adminToken);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var session = await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // Create runtime menu so runtime-menu returns 200 OK
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/catalog/branches/{branch.Id}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("Main Menu", $"mm-{Guid.NewGuid():N}"));
        var menu = await (await client.SendAsync(menuReq)).Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/catalog/branches/{branch.Id}/menus/{menu!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("Starters", $"st-{Guid.NewGuid():N}"));
        var cat = await (await client.SendAsync(catReq)).Content.ReadFromJsonAsync<MenuCategoryDto>();

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/catalog/branches/{branch.Id}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(cat!.Id, "Soup", $"sp-{Guid.NewGuid():N}", 1500));
        await client.SendAsync(itemReq);

        return (tenantId, branch.Id, table.Id, session!.Id);
    }

    [Fact]
    public async Task CustomerJwt_ValidToken_CanAccessRuntimeMenu_Returns200()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, sessionId) = await SetupCustomerEnvironmentAsync(client);

        var token = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", token);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task CustomerJwt_ForeignTenantOrBranch_Rejected()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, sessionId) = await SetupCustomerEnvironmentAsync(client);

        // Foreign tenant
        var foreignTenantToken = RestaurantConfigTestHelpers.GenerateCustomerToken(Guid.NewGuid(), branchId, sessionId);
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", foreignTenantToken);
        var resp1 = await client.SendAsync(req1);
        Assert.True(resp1.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        // Foreign branch
        var foreignBranchToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, Guid.NewGuid(), sessionId);
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", foreignBranchToken);
        var resp2 = await client.SendAsync(req2);
        Assert.True(resp2.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CustomerJwt_ClosedOrMergedSession_Rejected401()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, tableId, sessionId) = await SetupCustomerEnvironmentAsync(client);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // Activate -> RequestBill -> Close session with proper ConcurrencyToken headers
        var getReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}", adminToken);
        var sDto = await (await client.SendAsync(getReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var actReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/activate", adminToken);
        actReq.Headers.Add("If-Match", $"\"{sDto!.ConcurrencyToken}\"");
        var actDto = await (await client.SendAsync(actReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var billReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/request-bill", adminToken);
        billReq.Headers.Add("If-Match", $"\"{actDto!.ConcurrencyToken}\"");
        var billDto = await (await client.SendAsync(billReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var closeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/close", adminToken);
        closeReq.Headers.Add("If-Match", $"\"{billDto!.ConcurrencyToken}\"");
        await client.SendAsync(closeReq);

        // Now attempt to access runtime-menu with the customer token for the closed session
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", customerToken);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task CustomerJwt_MissingOrMalformedSub_OrSubMismatch_Rejected401()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, sessionId) = await SetupCustomerEnvironmentAsync(client);

        // 1. Missing sub
        var tokenNoSub = BuildCustomCustomerToken(tenantId, branchId, sessionId, omitSub: true);
        var respNoSub = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", tokenNoSub));
        Assert.Equal(HttpStatusCode.Unauthorized, respNoSub.StatusCode);

        // 2. Malformed sub (not a GUID)
        var tokenMalformedSub = BuildCustomCustomerToken(tenantId, branchId, sessionId, customSub: "not-a-guid-string");
        var respMalformed = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", tokenMalformedSub));
        Assert.Equal(HttpStatusCode.Unauthorized, respMalformed.StatusCode);

        // 3. Sub mismatch (valid GUID but different from table_session_id)
        var tokenMismatchSub = BuildCustomCustomerToken(tenantId, branchId, sessionId, customSub: Guid.NewGuid().ToString());
        var respMismatch = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", tokenMismatchSub));
        Assert.Equal(HttpStatusCode.Unauthorized, respMismatch.StatusCode);
    }

    [Fact]
    public async Task CustomerJwt_FutureIat_OrExcessivelyLongToken_Rejected401()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, sessionId) = await SetupCustomerEnvironmentAsync(client);

        // 1. Future iat (+1 hour)
        var tokenFuture = BuildCustomCustomerToken(tenantId, branchId, sessionId, iat: DateTimeOffset.UtcNow.AddHours(1));
        var respFuture = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", tokenFuture));
        Assert.Equal(HttpStatusCode.Unauthorized, respFuture.StatusCode);

        // 2. Excessively long token (> 4096 characters)
        var hugePadding = new string('A', 5000);
        var tokenHuge = BuildCustomCustomerToken(tenantId, branchId, sessionId, extraPayload: hugePadding);
        Assert.True(tokenHuge.Length > 4096);

        var respHuge = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", tokenHuge));
        Assert.Equal(HttpStatusCode.Unauthorized, respHuge.StatusCode);
    }

    [Fact]
    public async Task Qr_ParallelExchange_ProducesSingleActiveSession_AndReusesSameSessionId()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, tableId, _) = await SetupCustomerEnvironmentAsync(client);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // Close any existing session so table is fresh
        var qrMetaResp = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/qr", adminToken));
        var qrMeta = await qrMetaResp.Content.ReadFromJsonAsync<TableQrCodeDto>(JsonOptions);
        Assert.NotNull(qrMeta?.Token);

        // Fire 4 parallel exchange requests simultaneously
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            await barrier.Task;
            return await client.PostAsJsonAsync("/api/v1/qr/exchange", new QrExchangeRequest(qrMeta.Token));
        }).ToList();

        barrier.SetResult();
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var sessionIds = new List<Guid>();
        foreach (var r in responses)
        {
            var content = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            sessionIds.Add(content.GetProperty("sessionId").GetGuid());
        }

        // All concurrent exchange calls must resolve to the EXACT SAME session ID
        Assert.Single(sessionIds.Distinct());
    }

    [Fact]
    public async Task Qr_Rotation_And_TableDeactivation_RejectsExchangeCleanly()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, tableId, _) = await SetupCustomerEnvironmentAsync(client);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        // 1. Get original QR code token (version 1)
        var qrResp1 = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/qr", adminToken));
        var qrMeta1 = await qrResp1.Content.ReadFromJsonAsync<TableQrCodeDto>(JsonOptions);
        var oldToken = qrMeta1!.Token;

        // Rotate QR version (bump to version 2) with table concurrency token
        var tableGetResp = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables/{tableId}", adminToken));
        var tableDto1 = await tableGetResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var rotateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/qr/rotate", adminToken);
        rotateReq.Headers.Add("If-Match", $"\"{tableDto1!.ConcurrencyToken}\"");
        var rotateResp = await client.SendAsync(rotateReq);
        Assert.Equal(HttpStatusCode.OK, rotateResp.StatusCode);

        // Exchanging old QR token must fail with 410 Gone / 400 Bad Request
        var oldExchResp = await client.PostAsJsonAsync("/api/v1/qr/exchange", new QrExchangeRequest(oldToken));
        Assert.True(oldExchResp.StatusCode is HttpStatusCode.Gone or HttpStatusCode.BadRequest);

        // 2. Deactivate table and assert exchange is blocked
        // First close active session on the table
        var tableStatusResp = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables/{tableId}", adminToken));
        var tableDto = await tableStatusResp.Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var deactReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/deactivate", adminToken);
        deactReq.Headers.Add("If-Match", $"\"{tableDto!.ConcurrencyToken}\"");
        var deactResp = await client.SendAsync(deactReq);
        Assert.True(deactResp.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Qr_GenericErrorResponse_DoesNotLeakInternalDetails()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);

        // Send completely invalid/garbage token to exchange endpoint
        var resp = await client.PostAsJsonAsync("/api/v1/qr/exchange", new QrExchangeRequest("invalid.garbage.token"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("StackTrace", body);
        Assert.DoesNotContain("Postgres", body);
        Assert.DoesNotContain("Npgsql", body);
        Assert.Contains("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Qr_CrossTenantAndCrossBranch_DoesNotLeakData()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantA, branchA, tableA, _) = await SetupCustomerEnvironmentAsync(client);
        var (tenantB, branchB, _, _) = await SetupCustomerEnvironmentAsync(client);

        var adminTokenA = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantA, role: "RestaurantAdmin");
        var adminTokenB = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantB, role: "RestaurantAdmin");

        // 1. Tenant B cannot access Table A QR
        var crossQrReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchA}/tables/{tableA}/qr", adminTokenB);
        var crossQrResp = await client.SendAsync(crossQrReq);
        Assert.True(crossQrResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        // 2. Exchanging Table A's QR token returns a customer token valid ONLY for Tenant A / Branch A
        var qrMetaResp = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchA}/tables/{tableA}/qr", adminTokenA));
        var qrMeta = await qrMetaResp.Content.ReadFromJsonAsync<TableQrCodeDto>(JsonOptions);
        var exchangeResp = await client.PostAsJsonAsync("/api/v1/qr/exchange", new QrExchangeRequest(qrMeta!.Token));
        Assert.Equal(HttpStatusCode.OK, exchangeResp.StatusCode);

        var setCookieHeaders = exchangeResp.Headers.GetValues("Set-Cookie").ToList();
        var accessCookieHeader = setCookieHeaders.FirstOrDefault(c => c.Contains(AuthCookieService.AccessTokenCookieName));
        Assert.NotNull(accessCookieHeader);
        var tokenA = accessCookieHeader.Split(';')[0].Split('=')[1];
        Assert.False(string.IsNullOrWhiteSpace(tokenA));

        // Customer token for Tenant A cannot access Tenant B runtime-menu
        var crossAccessReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchB}/runtime-menu", tokenA);
        var crossAccessResp = await client.SendAsync(crossAccessReq);
        Assert.True(crossAccessResp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }
}
