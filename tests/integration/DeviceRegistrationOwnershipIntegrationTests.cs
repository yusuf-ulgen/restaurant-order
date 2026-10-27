using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class DeviceRegistrationOwnershipIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public DeviceRegistrationOwnershipIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, Guid table1Id, Guid session1Id, Guid table2Id, Guid session2Id)> SeedTablesAndSessionsAsync()
    {
        var tenantId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var table1Id = Guid.NewGuid();
        var session1Id = Guid.NewGuid();
        var table2Id = Guid.NewGuid();
        var session2Id = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
            VALUES (@tenantId, 'Device Reg Tenant', @tenantSlug, 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
            VALUES (@brandId, @tenantId, 'DR Brand', @brandSlug, 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
            VALUES (@branchId, @tenantId, @brandId, 'DR Branch', @branchSlug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
            VALUES (@areaId, @tenantId, @branchId, 'Main Floor', @areaSlug, 1, 1, true, NOW(), gen_random_uuid());

            INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, public_code, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
            VALUES (@table1Id, @tenantId, @branchId, @areaId, 'Masa-1', 'Table 1', md5(random()::text), 4, 100, 100, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW()),
                   (@table2Id, @tenantId, @branchId, @areaId, 'Masa-2', 'Table 2', md5(random()::text), 4, 200, 100, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW());

            INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, activated_at_utc, created_at_utc, concurrency_token)
            VALUES (@session1Id, @tenantId, @branchId, @table1Id, 2, 2, NOW(), NOW(), NOW(), gen_random_uuid()),
                   (@session2Id, @tenantId, @branchId, @table2Id, 2, 2, NOW(), NOW(), NOW(), gen_random_uuid());
        ";

        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("tenantSlug", $"t-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("brandId", brandId);
        cmd.Parameters.AddWithValue("brandSlug", $"b-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("branchSlug", $"br-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("areaId", areaId);
        cmd.Parameters.AddWithValue("areaSlug", $"a-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("table1Id", table1Id);
        cmd.Parameters.AddWithValue("session1Id", session1Id);
        cmd.Parameters.AddWithValue("table2Id", table2Id);
        cmd.Parameters.AddWithValue("session2Id", session2Id);

        await cmd.ExecuteNonQueryAsync();
        return (tenantId, branchId, table1Id, session1Id, table2Id, session2Id);
    }

    private async Task CloseSessionAsync(Guid sessionId)
    {
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE tenancy.dining_sessions
            SET status = 4,
                bill_requested_at_utc = NOW() + interval '1 second',
                closed_at_utc = NOW() + interval '2 seconds'
            WHERE id = @sessionId;";
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task RegisterDevice_SameOwnerRenewal_ReturnsOk()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, sessionId, _, _) = await SeedTablesAndSessionsAsync();

        var token = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var installationId = $"inst_same_owner_{Guid.NewGuid():N}";

        // 1. Initial Registration
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/notifications/devices", token);
        req1.Content = JsonContent.Create(new RegisterDeviceRequest(installationId, "web"));
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2. Same Owner Renewal
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/notifications/devices", token);
        req2.Content = JsonContent.Create(new RegisterDeviceRequest(installationId, "web"));
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_AnotherActiveSessionRenewal_ReturnsConflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, sessionA, _, sessionB) = await SeedTablesAndSessionsAsync();

        var tokenA = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionA);
        var tokenB = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionB);
        var installationId = $"inst_conflict_{Guid.NewGuid():N}";

        // Session A registers the device
        var regA = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/notifications/devices", tokenA);
        regA.Content = JsonContent.Create(new RegisterDeviceRequest(installationId, "web"));
        var resA = await client.SendAsync(regA);
        Assert.Equal(HttpStatusCode.OK, resA.StatusCode);

        // Session B attempts to register/renew the exact same installation ID while Session A is active
        var regB = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/notifications/devices", tokenB);
        regB.Content = JsonContent.Create(new RegisterDeviceRequest(installationId, "web"));
        var resB = await client.SendAsync(regB);

        Assert.Equal(HttpStatusCode.Conflict, resB.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_TableAToTableBSwitch_WhenTableAClosed_RebindsSuccessfully()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, sessionA, _, sessionB) = await SeedTablesAndSessionsAsync();

        var tokenA = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionA);
        var tokenB = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionB);
        var installationId = $"inst_shared_tablet_{Guid.NewGuid():N}";

        // Table A customer registers the shared tablet
        var regA = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/notifications/devices", tokenA);
        regA.Content = JsonContent.Create(new RegisterDeviceRequest(installationId, "android"));
        var resA = await client.SendAsync(regA);
        Assert.Equal(HttpStatusCode.OK, resA.StatusCode);

        // Table A pays and session is closed
        await CloseSessionAsync(sessionA);

        // Tablet is reassigned to Table B: Table B customer registers -> Safe rebind succeeds!
        var regB = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/notifications/devices", tokenB);
        regB.Content = JsonContent.Create(new RegisterDeviceRequest(installationId, "android"));
        var resB = await client.SendAsync(regB);
        Assert.Equal(HttpStatusCode.OK, resB.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_MissingClaim_ReturnsUnauthorized()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);

        // Completely unauthenticated
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/notifications/devices");
        req.Content = JsonContent.Create(new RegisterDeviceRequest("inst_missing_claim", "web"));
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_ConcurrentRegister_DoesNotReturn500()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, sessionId, _, _) = await SeedTablesAndSessionsAsync();

        var token = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var sharedInstallationId = $"inst_concurrent_{Guid.NewGuid():N}";

        // 5 concurrent registration requests for the same installation ID
        var tasks = Enumerable.Range(0, 5).Select(_ =>
        {
            var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/notifications/devices", token);
            req.Content = JsonContent.Create(new RegisterDeviceRequest(sharedInstallationId, "web"));
            return client.SendAsync(req);
        });

        var responses = await Task.WhenAll(tasks);

        // None of the requests should crash with 500 Internal Server Error (PostgreSQL 23505 unique violation handled)
        foreach (var resp in responses)
        {
            Assert.NotEqual(HttpStatusCode.InternalServerError, resp.StatusCode);
        }

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task RegisterDevice_CrossBranchConflict_And_CrossTenantIsolation()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenant1Id, branch1Id, _, session1Id, _, _) = await SeedTablesAndSessionsAsync();
        var (tenant2Id, branch2Id, _, session2Id, _, _) = await SeedTablesAndSessionsAsync();

        var sharedInstallationId = $"inst_isolation_{Guid.NewGuid():N}";
        var tokenT1B1 = RestaurantConfigTestHelpers.GenerateCustomerToken(tenant1Id, branch1Id, session1Id);
        var tokenT2B2 = RestaurantConfigTestHelpers.GenerateCustomerToken(tenant2Id, branch2Id, session2Id);

        // 1. Tenant 1 registers installation ID
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/notifications/devices", tokenT1B1);
        req1.Content = JsonContent.Create(new RegisterDeviceRequest(sharedInstallationId, "web"));
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2. Tenant 2 registers the exact same installation ID: RLS ensures clean isolation
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/notifications/devices", tokenT2B2);
        req2.Content = JsonContent.Create(new RegisterDeviceRequest(sharedInstallationId, "web"));
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
    }
}
