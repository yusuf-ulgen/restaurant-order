using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying:
/// 1. Highly concurrent static QR exchange serialization and atomic single-session acquisition without 25P02.
/// 2. Fresh and upgrade database migration constraints for CSPRNG public table code.
/// </summary>
public class FloorQrConcurrencyAndMigrationIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FloorQrConcurrencyAndMigrationIntegrationTests(TestcontainersFixture fixture)
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
    public async Task Concurrent_StaticQrExchange_OnSameTable_Succeeds_ReturnsSameSessionId_Without25P02()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);

        // 1. Setup tenant, branch, dining area, and table
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Conc Tenant", $"conc-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Conc Brand", $"cb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "Conc Branch", $"cbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Conc Area", $"ca-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var tableReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        tableReq.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "CONC-01", "Concurrent Table", 4));
        var table = await (await client.SendAsync(tableReq)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        // 2. Fetch signed static QR code
        var qrMetaResp = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branch.Id}/tables/{table!.Id}/qr", adminToken));
        var qrMeta = await qrMetaResp.Content.ReadFromJsonAsync<TableQrCodeDto>(JsonOptions);
        Assert.NotNull(qrMeta?.Token);

        // 3. Fire multiple concurrent exchange requests simultaneously for the same table
        const int concurrentCalls = 6;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, concurrentCalls).Select(async i =>
        {
            await barrier.Task;
            // Create independent client instance or request to simulate concurrent mobile clients
            var exchangeClient = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
            return await exchangeClient.PostAsJsonAsync("/api/v1/qr/exchange", new QrExchangeRequest(qrMeta.Token));
        }).ToList();

        // Release all requests concurrently
        barrier.SetResult();
        var responses = await Task.WhenAll(tasks);

        // 4. Assert: All parallel calls succeeded with 200 OK (no 500, no 25P02 transaction aborted errors)
        foreach (var resp in responses)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // 5. Assert: All parallel calls returned the EXACT same session ID
        var sessionIds = new List<Guid>();
        foreach (var resp in responses)
        {
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            var sid = doc.RootElement.GetProperty("sessionId").GetGuid();
            sessionIds.Add(sid);
        }

        var distinctSessionIds = sessionIds.Distinct().ToList();
        Assert.Single(distinctSessionIds);

        // 6. Direct database verification: exactly 1 non-closed session exists for this table
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tenancy.dining_sessions WHERE table_id = @tableId AND status <> 4;";
        cmd.Parameters.AddWithValue("tableId", table.Id);
        var activeSessionCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.Equal(1, activeSessionCount);
    }

    [Fact]
    public async Task TablePublicCode_CsprngAndConstraints_EnforcedByDatabase()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        // 1. Verify pgcrypto extension is installed
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pg_extension WHERE extname = 'pgcrypto';";
            var extCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.True(extCount >= 1, "pgcrypto extension must be installed.");
        }

        // 2. Verify column default is dropped (public_code has no default)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT column_default, is_nullable
                FROM information_schema.columns
                WHERE table_schema = 'tenancy' AND table_name = 'restaurant_tables' AND column_name = 'public_code';";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var columnDefault = reader.IsDBNull(0) ? null : reader.GetString(0);
            var isNullable = reader.GetString(1);

            Assert.Null(columnDefault);
            Assert.Equal("NO", isNullable);
        }

        // 3. Verify check constraint ck_restaurant_tables_public_code rejects invalid length or format and accepts valid 32-hex
        var tenantId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var areaId = Guid.NewGuid();

        await using (var seedCmd = conn.CreateCommand())
        {
            seedCmd.CommandText = @"
                INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
                VALUES (@tid, 'Migr Tenant', @tslug, 'Active', clock_timestamp(), gen_random_uuid());
                INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
                VALUES (@bndId, @tid, 'Migr Brand', @bndslug, 'Active', clock_timestamp(), gen_random_uuid());
                INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
                VALUES (@bid, @tid, @bndId, 'Migr Branch', @brslug, 'Europe/Istanbul', 'TRY', 'Active', clock_timestamp(), gen_random_uuid());
                INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
                VALUES (@aid, @tid, @bid, 'Migr Area', @daslug, 1, 1, true, clock_timestamp(), gen_random_uuid());";
            seedCmd.Parameters.AddWithValue("tid", tenantId);
            seedCmd.Parameters.AddWithValue("tslug", $"mt-{Guid.NewGuid():N}");
            seedCmd.Parameters.AddWithValue("bndId", brandId);
            seedCmd.Parameters.AddWithValue("bndslug", $"mb-{Guid.NewGuid():N}");
            seedCmd.Parameters.AddWithValue("bid", branchId);
            seedCmd.Parameters.AddWithValue("brslug", $"mbr-{Guid.NewGuid():N}");
            seedCmd.Parameters.AddWithValue("aid", areaId);
            seedCmd.Parameters.AddWithValue("daslug", $"ma-{Guid.NewGuid():N}");
            await seedCmd.ExecuteNonQueryAsync();
        }

        // Inserting invalid short public_code must fail check constraint
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO tenancy.restaurant_tables (
                    id, tenant_id, branch_id, dining_area_id, table_number, name,
                    capacity, position_x, position_y, width, height, rotation_degrees,
                    shape, is_active, qr_version, public_code, concurrency_token, created_at
                ) VALUES (
                    @id, @tid, @bid, @aid, 'INV-01', 'Inv Table',
                    4, 0, 0, 100, 100, 0, 1, true, 1, 'short', @token, clock_timestamp()
                );";
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("bid", branchId);
            cmd.Parameters.AddWithValue("aid", areaId);
            cmd.Parameters.AddWithValue("token", Guid.NewGuid());

            var pgEx = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, pgEx.SqlState);
            Assert.Contains("ck_restaurant_tables_public_code", pgEx.ConstraintName);
        }

        // Inserting valid 32-char lowercase hex public_code must succeed
        var validHex32 = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        await using (var cmdValid = conn.CreateCommand())
        {
            cmdValid.CommandText = @"
                INSERT INTO tenancy.restaurant_tables (
                    id, tenant_id, branch_id, dining_area_id, table_number, name,
                    capacity, position_x, position_y, width, height, rotation_degrees,
                    shape, is_active, qr_version, public_code, concurrency_token, created_at
                ) VALUES (
                    @id, @tid, @bid, @aid, 'VAL-01', 'Val Table',
                    4, 0, 0, 100, 100, 0, 1, true, 1, @pcode, @token, clock_timestamp()
                );";
            cmdValid.Parameters.AddWithValue("id", Guid.NewGuid());
            cmdValid.Parameters.AddWithValue("tid", tenantId);
            cmdValid.Parameters.AddWithValue("bid", branchId);
            cmdValid.Parameters.AddWithValue("aid", areaId);
            cmdValid.Parameters.AddWithValue("pcode", validHex32);
            cmdValid.Parameters.AddWithValue("token", Guid.NewGuid());

            var inserted = await cmdValid.ExecuteNonQueryAsync();
            Assert.Equal(1, inserted);
        }
    }
}
