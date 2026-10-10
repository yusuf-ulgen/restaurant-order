using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Auth;
using RestaurantOrder.Infrastructure.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// End-to-end integration tests verifying PostgreSQL Row-Level Security (RLS) enforcement
/// under the unprivileged runtime role ('restaurant_app_user' / 'restaurant_app_runtime' with NOBYPASSRLS)
/// for Public QR resolve, QR exchange, Customer JWT claim validation, and Double-Submit CSRF.
/// </summary>
public class FloorQrAndCustomerAuthRlsIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FloorQrAndCustomerAuthRlsIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<string> GetRuntimeConnectionStringAsync()
    {
        await EnsureMigrationsAppliedAsync();
        return await _fixture.ProvisionTemporaryRuntimeRoleAsync();
    }

    private async Task<RestaurantOrderDbContext> CreateRuntimeDbContextAsync(ITenantContext? tenantContext = null)
    {
        var connStr = await GetRuntimeConnectionStringAsync();
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new RestaurantOrderDbContext(options, tenantContext);
    }

    private static (IQrSecurityService qrSecurity, IQrRateLimiter rateLimiter, IJwtTokenGenerator jwtGen) CreateFloorDependencies()
    {
        var qrOpts = Microsoft.Extensions.Options.Options.Create(new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }
        });
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var qrSecurity = new QrSecurityService(qrOpts, envMock.Object);

        var rateLimiterMock = new Mock<IQrRateLimiter>();
        rateLimiterMock.Setup(r => r.EnsureNotRateLimitedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jwtOpts = Microsoft.Extensions.Options.Options.Create(new JwtSettings
        {
            Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Issuer = "RestaurantOrder.Api",
            Audience = "RestaurantOrder.Client",
            AccessTokenLifetimeMinutes = 15
        });
        var jwtGen = new JwtTokenService(jwtOpts);

        return (qrSecurity, rateLimiterMock.Object, jwtGen);
    }

    [Fact]
    public async Task PublicQrResolve_And_Exchange_UnderRuntimeRole_WithForcedRls_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var publicCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        // 1. Seed tenant, branch, dining area and table using owner connection
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "RLS Test Tenant", $"rtt-{Guid.NewGuid():N}");
        await using (var adminCtx = new RestaurantOrderDbContext(
            new DbContextOptionsBuilder<RestaurantOrderDbContext>().UseNpgsql(_fixture.DatabaseConnectionString).Options))
        {
            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
                VALUES ({0}, {1}, 'RLS Brand', {2}, 'Active', NOW(), gen_random_uuid());
            ", brandId, tenantId, $"br-{Guid.NewGuid():N}");

            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
                VALUES ({0}, {1}, {2}, 'RLS Branch', {3}, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid());
            ", branchId, tenantId, brandId, $"brn-{Guid.NewGuid():N}");

            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
                VALUES ({0}, {1}, {2}, 'RLS Area', {3}, 1, 0, true, NOW(), gen_random_uuid());
            ", areaId, tenantId, branchId, $"da-{Guid.NewGuid():N}");

            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, public_code, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
                VALUES ({0}, {1}, {2}, {3}, 'T-RLS', 'Table RLS', {4}, 4, 0, 0, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW());
            ", tableId, tenantId, branchId, areaId, publicCode);
        }

        var (qrSecurity, rateLimiter, jwtGen) = CreateFloorDependencies();
        var payload = QrPayload.CreateStatic(
            qrSecurity.CurrentKeyId,
            new TenantId(tenantId),
            new BranchId(branchId),
            publicCode,
            qrVersion: 1);
        var token = qrSecurity.GenerateToken(payload);

        // 2. Resolve QR using unprivileged runtime connection string
        await using var runtimeCtx = await CreateRuntimeDbContextAsync();
        var qrService = new QrPublicService(
            runtimeCtx,
            qrSecurity,
            rateLimiter,
            jwtGen,
            NullLogger<QrPublicService>.Instance);

        var resolveResult = await qrService.ResolveQrAsync(token, "127.0.0.1");
        Assert.NotNull(resolveResult);
        Assert.Equal("T-RLS", resolveResult.TableNumber);
        Assert.Equal("RLS Branch", resolveResult.BranchName);
        Assert.Equal("RLS Test Tenant", resolveResult.BrandName);
        Assert.False(resolveResult.HasActiveSession);

        // 3. Static Exchange QR using unprivileged runtime connection string
        var exchangeResult = await qrService.ExchangeQrAsync(token, "127.0.0.1");
        Assert.NotNull(exchangeResult);
        Assert.Equal("Open", exchangeResult.SessionStatus);
        Assert.False(string.IsNullOrWhiteSpace(exchangeResult.AccessToken));

        // 4. Dynamic Exchange with newly opened session
        var dynamicPayload = QrPayload.CreateDynamic(
            qrSecurity.CurrentKeyId,
            new TenantId(tenantId),
            new BranchId(branchId),
            publicCode,
            qrVersion: 1,
            tableSessionId: new DiningSessionId(exchangeResult.SessionId),
            expiresAtUnix: DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds());
        var dynamicToken = qrSecurity.GenerateToken(dynamicPayload);

        var dynamicExchange = await qrService.ExchangeQrAsync(dynamicToken, "127.0.0.1");
        Assert.NotNull(dynamicExchange);
        Assert.Equal(exchangeResult.SessionId, dynamicExchange.SessionId);
    }

    [Fact]
    public async Task CustomerSessionValidator_UnderRuntimeRole_AcceptsMatchingClaims_AndRejectsCrossTenantOrBranch()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var brandA = Guid.NewGuid();
        var brandB = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var tableA = Guid.NewGuid();
        var sessionAId = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-{Guid.NewGuid():N}");

        await using (var adminCtx = new RestaurantOrderDbContext(
            new DbContextOptionsBuilder<RestaurantOrderDbContext>().UseNpgsql(_fixture.DatabaseConnectionString).Options))
        {
            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
                VALUES ({0}, {1}, 'Brand A', {2}, 'Active', NOW(), gen_random_uuid()),
                       ({3}, {4}, 'Brand B', {5}, 'Active', NOW(), gen_random_uuid());
            ", brandA, tenantA, $"ba-{Guid.NewGuid():N}", brandB, tenantB, $"bb-{Guid.NewGuid():N}");

            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
                VALUES ({0}, {1}, {2}, 'Branch A', {3}, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid()),
                       ({4}, {5}, {6}, 'Branch B', {7}, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid());
            ", branchA, tenantA, brandA, $"bra-{Guid.NewGuid():N}", branchB, tenantB, brandB, $"brb-{Guid.NewGuid():N}");

            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
                VALUES ({0}, {1}, {2}, 'Area A', {3}, 1, 0, true, NOW(), gen_random_uuid()),
                       ({4}, {5}, {6}, 'Area B', {7}, 1, 0, true, NOW(), gen_random_uuid());
            ", areaA, tenantA, branchA, $"daa-{Guid.NewGuid():N}", areaB, tenantB, branchB, $"dab-{Guid.NewGuid():N}");

            var publicCodeA = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, public_code, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
                VALUES ({0}, {1}, {2}, {3}, 'T-A', 'Table A', {4}, 4, 0, 0, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW());
            ", tableA, tenantA, branchA, areaA, publicCodeA);

            await adminCtx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, concurrency_token, created_at_utc)
                VALUES ({0}, {1}, {2}, {3}, 1, 2, NOW(), gen_random_uuid(), NOW());
            ", sessionAId, tenantA, branchA, tableA);
        }

        await using var runtimeCtx = await CreateRuntimeDbContextAsync();
        var redisMock = new Mock<IRedisDatabaseProvider>();
        redisMock.Setup(r => r.GetDatabaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IDatabase?)null);
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var validator = new CustomerSessionValidator(
            runtimeCtx,
            redisMock.Object,
            envMock.Object,
            NullLogger<CustomerSessionValidator>.Instance);

        // 1. Correct tenant, branch, and session -> Accepted (True)
        var valid = await validator.ValidateSessionActiveAsync(tenantA, branchA, sessionAId);
        Assert.True(valid);

        // 2. Cross-tenant combination (Tenant B querying Session A) -> Rejected (False)
        var crossTenant = await validator.ValidateSessionActiveAsync(tenantB, branchA, sessionAId);
        Assert.False(crossTenant);

        // 3. Cross-branch combination (Branch B querying Session A) -> Rejected (False)
        var crossBranch = await validator.ValidateSessionActiveAsync(tenantA, branchB, sessionAId);
        Assert.False(crossBranch);

        // 4. Closed session -> Rejected (False)
        await using (var adminCtx = new RestaurantOrderDbContext(
            new DbContextOptionsBuilder<RestaurantOrderDbContext>().UseNpgsql(_fixture.DatabaseConnectionString).Options))
        {
            await adminCtx.Database.ExecuteSqlRawAsync(@"
                UPDATE tenancy.dining_sessions SET status = 4, activated_at_utc = NOW(), bill_requested_at_utc = NOW(), closed_at_utc = NOW() WHERE id = {0};
            ", sessionAId);
        }

        var closedValid = await validator.ValidateSessionActiveAsync(tenantA, branchA, sessionAId);
        Assert.False(closedValid);
    }

    [Fact]
    public async Task CustomerSession_EndToEnd_SetsCsrfCookie_AndRequiresCsrfOnMutation()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);

        // 1. Setup tenant, branch, and table
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "CSRF Tenant", $"csrf-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("CSRF Brand", $"csb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "CSRF Branch", $"csbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("CSRF Area", $"csa-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var tableReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        tableReq.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "CSRF-01", "CSRF Table", 4));
        var table = await (await client.SendAsync(tableReq)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        // 2. Obtain QR code
        var qrMetaResp = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branch.Id}/tables/{table!.Id}/qr", adminToken));
        var qrMeta = await qrMetaResp.Content.ReadFromJsonAsync<TableQrCodeDto>(JsonOptions);

        // 3. Exchange QR token as customer
        var exchangeResp = await client.PostAsJsonAsync("/api/v1/qr/exchange", new QrExchangeRequest(qrMeta!.Token));
        Assert.Equal(HttpStatusCode.OK, exchangeResp.StatusCode);

        // Verify Set-Cookie headers contain both access token and CSRF token
        var setCookieHeaders = exchangeResp.Headers.GetValues("Set-Cookie").ToList();
        var accessCookieHeader = setCookieHeaders.FirstOrDefault(c => c.Contains(AuthCookieService.AccessTokenCookieName));
        var csrfCookieHeader = setCookieHeaders.FirstOrDefault(c => c.Contains(AuthCookieService.CsrfTokenCookieName));

        Assert.NotNull(accessCookieHeader);
        Assert.NotNull(csrfCookieHeader);
        Assert.Contains("HttpOnly", accessCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpOnly", csrfCookieHeader, StringComparison.OrdinalIgnoreCase);

        // Extract CSRF token value from cookie
        var csrfCookieValue = csrfCookieHeader.Split(';')[0].Split('=')[1];

        // 4. Subsequent mutation with ambient cookies:
        // Test missing CSRF header -> 403 Forbidden
        var missingCsrfReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/pin/login");
        missingCsrfReq.Headers.Add("Cookie", $"{AuthCookieService.AccessTokenCookieName}=sample; {AuthCookieService.CsrfTokenCookieName}={csrfCookieValue}");
        var missingCsrfResp = await client.SendAsync(missingCsrfReq);
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrfResp.StatusCode);

        // Test mismatched CSRF header -> 403 Forbidden
        var badCsrfReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/pin/login");
        badCsrfReq.Headers.Add("Cookie", $"{AuthCookieService.AccessTokenCookieName}=sample; {AuthCookieService.CsrfTokenCookieName}={csrfCookieValue}");
        badCsrfReq.Headers.Add(AuthCookieService.CsrfHeaderName, "tampered_token");
        var badCsrfResp = await client.SendAsync(badCsrfReq);
        Assert.Equal(HttpStatusCode.Forbidden, badCsrfResp.StatusCode);
    }
}
