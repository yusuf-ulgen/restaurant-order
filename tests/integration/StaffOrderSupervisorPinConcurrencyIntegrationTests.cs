using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Auth;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// PostgreSQL integration tests verifying supervisor PIN pessimistic concurrency locking (FOR UPDATE)
/// across multiple distinct dining sessions under the same tenant and branch.
/// </summary>
public class StaffOrderSupervisorPinConcurrencyIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IPinHasher PinHasher = new PepperedPinHasher(
        Options.Create(new PinHasherOptions
        {
            PepperKeyId = RestaurantConfigTestHelpers.SharedTestPinPepperKeyId,
            PepperValue = RestaurantConfigTestHelpers.SharedPinPepper,
            Environment = "Development"
        }));

    public StaffOrderSupervisorPinConcurrencyIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<Guid> SeedSupervisorAsync(
        Guid tenantId,
        Guid branchId,
        AuthRole role,
        string rawPin)
    {
        var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString).Options;
        await using var db = new RestaurantOrderDbContext(dbOptions);

        await using var tx = await db.BeginTenantTransactionAsync(tenantId);

        var userId = UserId.New();
        var email = $"sup-concur-{userId.Value:N}@test.com";
        var nowUtc = DateTimeOffset.UtcNow;
        var user = User.Create(email, "InsecureDevPassword123!", nowUtc, id: userId);
        db.Users.Add(user);

        var membership = UserMembership.Create(
            new TenantId(tenantId),
            userId,
            role,
            new BranchId(branchId),
            nowUtc,
            initialStatus: UserMembershipStatus.Active);
        db.Memberships.Add(membership);

        var hashed = PinHasher.HashPin(rawPin);
        var pinCred = PinCredential.Create(
            new TenantId(tenantId),
            userId,
            new BranchId(branchId),
            hashed.Hash,
            hashed.AlgorithmVersion,
            hashed.PepperKeyId,
            nowUtc);
        db.PinCredentials.Add(pinCred);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return userId.Value;
    }

    [Fact]
    public async Task ConcurrentFailedAttempts_AcrossDifferentSessions_AllCountedAndCredentialLocked()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, brandId, branchId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var areaId = Guid.NewGuid();
        var (menuId, categoryId, menuItemId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await using (var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
                VALUES (@tenantId, 'Concur Tenant', @tenantSlug, 'Active', NOW(), gen_random_uuid());
                INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
                VALUES (@brandId, @tenantId, 'Concur Brand', @brandSlug, 'Active', NOW(), gen_random_uuid());
                INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
                VALUES (@branchId, @tenantId, @brandId, 'Concur Branch', @branchSlug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid());
                INSERT INTO tenancy.branch_settings (id, tenant_id, branch_id, timezone, currency, default_locale, supported_locales, prices_include_tax, default_tax_rate_bps, is_service_charge_enabled, service_charge_rate_bps, is_order_taking_enabled, created_at, concurrency_token)
                VALUES (gen_random_uuid(), @tenantId, @branchId, 'Europe/Istanbul', 'TRY', 'tr-TR', '[""tr-TR""]', true, 1000, false, 0, true, NOW(), gen_random_uuid());
                INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
                VALUES (@areaId, @tenantId, @branchId, 'Main Hall', @areaSlug, 1, 1, true, NOW(), gen_random_uuid());
                INSERT INTO tenancy.menus (id, tenant_id, branch_id, name, slug, status, sort_order, created_at, concurrency_token)
                VALUES (@menuId, @tenantId, @branchId, 'Concur Menu', @menuSlug, 2, 1, NOW(), gen_random_uuid());
                INSERT INTO tenancy.menu_categories (id, tenant_id, branch_id, menu_id, name, slug, sort_order, is_active, created_at, concurrency_token)
                VALUES (@categoryId, @tenantId, @branchId, @menuId, 'Main Dishes', @catSlug, 1, true, NOW(), gen_random_uuid());
                INSERT INTO tenancy.menu_items (id, tenant_id, branch_id, menu_id, category_id, name, slug, base_price_minor_units, sort_order, is_active, created_at, concurrency_token)
                VALUES (@menuItemId, @tenantId, @branchId, @menuId, @categoryId, 'Steak', @itemSlug, 4000, 1, true, NOW(), gen_random_uuid());";

            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("tenantSlug", $"t-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("brandId", brandId);
            cmd.Parameters.AddWithValue("brandSlug", $"b-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("branchId", branchId);
            cmd.Parameters.AddWithValue("branchSlug", $"br-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("areaId", areaId);
            cmd.Parameters.AddWithValue("areaSlug", $"a-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("menuId", menuId);
            cmd.Parameters.AddWithValue("menuSlug", $"m-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("categoryId", categoryId);
            cmd.Parameters.AddWithValue("catSlug", $"c-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("menuItemId", menuItemId);
            cmd.Parameters.AddWithValue("itemSlug", $"i-{Guid.NewGuid():N}");
            await cmd.ExecuteNonQueryAsync();
        }

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        // 1. Create 5 distinct dining sessions, each on its own distinct table with its own order and in-prep item
        const int concurrentCount = 5;
        var sessionOrders = new List<(Guid sessionId, Guid orderId, Guid itemId, string token)>();

        for (int i = 0; i < concurrentCount; i++)
        {
            var tableId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            await using (var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, public_code, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
                    VALUES (@tableId, @tenantId, @branchId, @areaId, @tableNumber, @tableName, md5(random()::text), 4, 100, 100, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW());
                    INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, activated_at_utc, created_at_utc, concurrency_token)
                    VALUES (@sessionId, @tenantId, @branchId, @tableId, 2, 2, NOW(), NOW(), NOW(), gen_random_uuid());";
                cmd.Parameters.AddWithValue("sessionId", sessionId);
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("branchId", branchId);
                cmd.Parameters.AddWithValue("areaId", areaId);
                cmd.Parameters.AddWithValue("tableId", tableId);
                cmd.Parameters.AddWithValue("tableNumber", $"T-0{i + 1}");
                cmd.Parameters.AddWithValue("tableName", $"Table {i + 1}");
                await cmd.ExecuteNonQueryAsync();
            }

            var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
                HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders", waiterToken);
            createReq.Headers.Add("Idempotency-Key", $"concur-order-{i}-{Guid.NewGuid():N}");
            createReq.Content = JsonContent.Create(new CreateStaffOrderRequest(
                new List<StaffOrderItemRequest>
                {
                    new(menuItemId, null, 1, null, null, 4000)
                },
                ClientPricingPolicyFingerprint: "0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9",
                ClientExpectedTotalMinorUnits: 4000));

            var createRes = await client.SendAsync(createReq);
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var created = await createRes.Content.ReadFromJsonAsync<StaffOrderResponseDto>(JsonOptions);
            Assert.NotNull(created);

            string currentToken;
            var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
                .UseNpgsql(_fixture.DatabaseConnectionString).Options;
            await using (var db = new RestaurantOrderDbContext(dbOptions))
            {
                await using var tx = await db.BeginTenantTransactionAsync(tenantId);
                var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Id));
                order.StartPreparation();
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                currentToken = order.ConcurrencyToken.ToString("D");
            }

            sessionOrders.Add((sessionId, created.Id, created.Items[0].Id, currentToken));
        }

        // 2. Seed 1 supervisor credential for branch manager
        var managerId = await SeedSupervisorAsync(tenantId, branchId, AuthRole.BranchManager, "1234");

        // 3. Coordinate simultaneous execution of 5 failed PIN requests on 5 DIFFERENT sessions
        var readySignal = new CountdownEvent(concurrentCount);
        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startTimeUtc = DateTimeOffset.UtcNow;

        var tasks = sessionOrders.Select(async sessionData =>
        {
            readySignal.Signal();
            await startSignal.Task;

            var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
                HttpMethod.Post,
                $"/api/v1/floor/branches/{branchId}/sessions/{sessionData.sessionId}/orders/{sessionData.orderId}/items/{sessionData.itemId}/cancel",
                waiterToken);
            req.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{sessionData.token}\""));
            req.Content = JsonContent.Create(new CancelOrderItemRequest("Eşzamanlı yanlış deneme", SupervisorPin: "0000", SupervisorUserId: managerId));
            return await client.SendAsync(req);
        }).ToList();

        readySignal.Wait();
        startSignal.SetResult();

        var responses = await Task.WhenAll(tasks);

        // 4. All 5 requests must fail with HTTP 403 Forbidden
        foreach (var r in responses)
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }

        // 5. Verify database: failed_pin_attempts is exactly 5 and locked_until_utc is within 15-minute window
        await using (var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT failed_pin_attempts, locked_until_utc FROM iam.pin_credentials WHERE tenant_id = @tenantId AND user_id = @userId";
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("userId", managerId);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var attempts = reader.GetInt32(0);
            Assert.False(reader.IsDBNull(1));
            var lockedUntil = reader.GetDateTime(1);

            Assert.Equal(5, attempts);
            Assert.True(lockedUntil >= startTimeUtc.AddMinutes(14).UtcDateTime, "Lockout window must be >= 14 minutes from start");
            Assert.True(lockedUntil <= startTimeUtc.AddMinutes(16).UtcDateTime, "Lockout window must be <= 16 minutes from start");
        }

        // 6. Subsequent request with the CORRECT PIN must be rejected due to lockout
        var lockedReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionOrders[0].sessionId}/orders/{sessionOrders[0].orderId}/items/{sessionOrders[0].itemId}/cancel",
            waiterToken);
        lockedReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{sessionOrders[0].token}\""));
        lockedReq.Content = JsonContent.Create(new CancelOrderItemRequest("Kilitliyken doğru PIN denemesi", SupervisorPin: "1234", SupervisorUserId: managerId));

        var lockedRes = await client.SendAsync(lockedReq);
        Assert.Equal(HttpStatusCode.Forbidden, lockedRes.StatusCode);

        // 7. Verify zero mutations and zero WASTE_CANCEL audits across all 5 orders
        var checkDbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString).Options;
        await using (var db = new RestaurantOrderDbContext(checkDbOptions))
        {
            await using var tx = await db.BeginTenantTransactionAsync(tenantId);
            foreach (var s in sessionOrders)
            {
                var ord = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(s.orderId));
                Assert.Equal(OrderStatus.InPreparation, ord.Status);
                Assert.Equal(OrderItemStatus.InPreparation, ord.Items.First().Status);

                var audits = await db.OrderAuditEntries.Where(a => a.OrderId == new OrderId(s.orderId) && a.Action == "WASTE_CANCEL").ToListAsync();
                Assert.Empty(audits);
            }
        }
    }
}
