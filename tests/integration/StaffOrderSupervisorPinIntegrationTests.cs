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
/// PostgreSQL integration tests verifying supervisor PIN verification,
/// branch manager scoping, isolation, and audit redaction.
/// </summary>
public class StaffOrderSupervisorPinIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IPinHasher PinHasher = new PepperedPinHasher(
        Options.Create(new PinHasherOptions
        {
            PepperKeyId = "dev_v1",
            PepperValue = "0123456789abcdef0123456789abcdef",
            Environment = "Development"
        }));

    public StaffOrderSupervisorPinIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task EnsureMigrationsAppliedAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString, n => n.MigrationsAssembly(typeof(RestaurantOrderDbContext).Assembly.FullName))
            .Options;
        await using var context = new RestaurantOrderDbContext(options);
        await context.Database.MigrateAsync();
    }

    private async Task<(Guid tenantId, Guid branchId, Guid sessionId, Guid orderId, Guid itemId, string concurrencyToken)> SetupOrderInPrepAsync()
    {
        var (tenantId, brandId, branchId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (areaId, tableId, sessionId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (menuId, categoryId, menuItemId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
            VALUES (@tenantId, 'Sup Tenant', @tenantSlug, 'Active', NOW(), gen_random_uuid());
            INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
            VALUES (@brandId, @tenantId, 'Sup Brand', @brandSlug, 'Active', NOW(), gen_random_uuid());
            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
            VALUES (@branchId, @tenantId, @brandId, 'Sup Branch', @branchSlug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid());
            INSERT INTO tenancy.branch_settings (id, tenant_id, branch_id, timezone, currency, default_locale, supported_locales, prices_include_tax, default_tax_rate_bps, is_service_charge_enabled, service_charge_rate_bps, is_order_taking_enabled, created_at, concurrency_token)
            VALUES (gen_random_uuid(), @tenantId, @branchId, 'Europe/Istanbul', 'TRY', 'tr-TR', '[""tr-TR""]', true, 1000, false, 0, true, NOW(), gen_random_uuid());
            INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
            VALUES (@areaId, @tenantId, @branchId, 'Main Hall', @areaSlug, 1, 1, true, NOW(), gen_random_uuid());
            INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, public_code, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
            VALUES (@tableId, @tenantId, @branchId, @areaId, 'T-01', 'Table 1', md5(random()::text), 4, 100, 100, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW());
            INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, activated_at_utc, created_at_utc, concurrency_token)
            VALUES (@sessionId, @tenantId, @branchId, @tableId, 2, 2, NOW(), NOW(), NOW(), gen_random_uuid());
            INSERT INTO tenancy.menus (id, tenant_id, branch_id, name, slug, status, sort_order, created_at, concurrency_token)
            VALUES (@menuId, @tenantId, @branchId, 'Sup Menu', @menuSlug, 2, 1, NOW(), gen_random_uuid());
            INSERT INTO tenancy.menu_categories (id, tenant_id, branch_id, menu_id, name, slug, sort_order, is_active, created_at, concurrency_token)
            VALUES (@categoryId, @tenantId, @branchId, @menuId, 'Main Dishes', @catSlug, 1, true, NOW(), gen_random_uuid());
            INSERT INTO tenancy.menu_items (id, tenant_id, branch_id, menu_id, category_id, name, slug, base_price_minor_units, sort_order, is_active, created_at, concurrency_token)
            VALUES (@menuItemId, @tenantId, @branchId, @menuId, @categoryId, 'Kebab', @itemSlug, 3000, 1, true, NOW(), gen_random_uuid());";

        var parameters = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId, ["tenantSlug"] = $"t-{Guid.NewGuid():N}",
            ["brandId"] = brandId, ["brandSlug"] = $"b-{Guid.NewGuid():N}",
            ["branchId"] = branchId, ["branchSlug"] = $"br-{Guid.NewGuid():N}",
            ["areaId"] = areaId, ["areaSlug"] = $"a-{Guid.NewGuid():N}",
            ["tableId"] = tableId, ["sessionId"] = sessionId,
            ["menuId"] = menuId, ["menuSlug"] = $"m-{Guid.NewGuid():N}",
            ["categoryId"] = categoryId, ["catSlug"] = $"c-{Guid.NewGuid():N}",
            ["menuItemId"] = menuItemId, ["itemSlug"] = $"i-{Guid.NewGuid():N}"
        };
        foreach (var (k, v) in parameters) cmd.Parameters.AddWithValue(k, v);
        await cmd.ExecuteNonQueryAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");
        var createReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders", waiterToken);
        createReq.Headers.Add("Idempotency-Key", $"sup-test-order-{Guid.NewGuid():N}");
        createReq.Content = JsonContent.Create(new CreateStaffOrderRequest(
            new List<StaffOrderItemRequest>
            {
                new(menuItemId, null, 1, "Müşteri acısız istiyor", null, 3000)
            },
            ClientPricingPolicyFingerprint: "0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9",
            ClientExpectedTotalMinorUnits: 3000));

        var createRes = await client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<StaffOrderResponseDto>(JsonOptions);
        Assert.NotNull(created);

        var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString).Options;
        await using var db = new RestaurantOrderDbContext(dbOptions);
        await using var tx = await db.BeginTenantTransactionAsync(tenantId);
        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == new OrderId(created.Id));
        order.StartPreparation();
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return (tenantId, branchId, sessionId, created.Id, created.Items[0].Id, order.ConcurrencyToken.ToString("D"));
    }

    private async Task<Guid> SeedSupervisorAsync(
        Guid tenantId,
        Guid? branchId,
        AuthRole role,
        string rawPin,
        UserMembershipStatus status = UserMembershipStatus.Active)
    {
        var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString).Options;
        await using var db = new RestaurantOrderDbContext(dbOptions);

        await using var tx = await db.BeginTenantTransactionAsync(tenantId);

        var userId = UserId.New();
        var email = $"sup-{userId.Value:N}@test.com";
        var nowUtc = DateTimeOffset.UtcNow;
        var user = User.Create(email, "InsecureDevPassword123!", nowUtc, id: userId);
        db.Users.Add(user);
        var membership = UserMembership.Create(
            new TenantId(tenantId),
            userId,
            role,
            branchId.HasValue ? new BranchId(branchId.Value) : null,
            nowUtc,
            initialStatus: status);
        db.Memberships.Add(membership);

        if (branchId.HasValue)
        {
            var hashed = PinHasher.HashPin(rawPin);
            var pinCred = PinCredential.Create(
                new TenantId(tenantId),
                userId,
                new BranchId(branchId.Value),
                hashed.Hash,
                hashed.AlgorithmVersion,
                hashed.PepperKeyId,
                nowUtc);
            db.PinCredentials.Add(pinCred);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return userId.Value;
    }

    [Fact]
    public async Task VerifySupervisorPin_ValidBranchManagerPin_AllowsVoid()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, sessionId, orderId, itemId, token) = await SetupOrderInPrepAsync();
        var managerId = await SeedSupervisorAsync(tenantId, branchId, AuthRole.BranchManager, "4567");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        var voidReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel",
            waiterToken);
        voidReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{token}\""));
        voidReq.Content = JsonContent.Create(new CancelOrderItemRequest("Müşteri beklemedi", SupervisorPin: "4567", SupervisorUserId: managerId));

        var res = await client.SendAsync(voidReq);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<StaffOrderResponseDto>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("Cancelled", body.Items[0].Status);
    }

    [Fact]
    public async Task VerifySupervisorPin_DifferentBranchManagerPin_Rejects()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, sessionId, orderId, itemId, token) = await SetupOrderInPrepAsync();
        var otherBranchId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token) VALUES (@otherBranchId, @tenantId, (SELECT brand_id FROM tenancy.branches WHERE id = @branchId), 'Other Branch', @branchSlug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid());";
        cmd.Parameters.AddWithValue("otherBranchId", otherBranchId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("branchSlug", $"br-other-{Guid.NewGuid():N}");
        await cmd.ExecuteNonQueryAsync();

        var otherManagerId = await SeedSupervisorAsync(tenantId, otherBranchId, AuthRole.BranchManager, "4567");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        var voidReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel",
            waiterToken);
        voidReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{token}\""));
        voidReq.Content = JsonContent.Create(new CancelOrderItemRequest("Müşteri beklemedi", SupervisorPin: "4567", SupervisorUserId: otherManagerId));

        var res = await client.SendAsync(voidReq);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task VerifySupervisorPin_SuspendedManager_Rejects()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, sessionId, orderId, itemId, token) = await SetupOrderInPrepAsync();
        var suspendedManagerId = await SeedSupervisorAsync(tenantId, branchId, AuthRole.BranchManager, "4567", status: UserMembershipStatus.Suspended);

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        var voidReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel",
            waiterToken);
        voidReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{token}\""));
        voidReq.Content = JsonContent.Create(new CancelOrderItemRequest("Müşteri beklemedi", SupervisorPin: "4567", SupervisorUserId: suspendedManagerId));

        var res = await client.SendAsync(voidReq);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task VerifySupervisorPin_RestaurantAdminPin_Rejects()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, sessionId, orderId, itemId, token) = await SetupOrderInPrepAsync();
        var adminId = await SeedSupervisorAsync(tenantId, null, AuthRole.RestaurantAdmin, "4567");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        var voidReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel",
            waiterToken);
        voidReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{token}\""));
        voidReq.Content = JsonContent.Create(new CancelOrderItemRequest("Admin PIN denemesi", SupervisorPin: "4567", SupervisorUserId: adminId));

        var res = await client.SendAsync(voidReq);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Void_RestaurantAdminDirectAuthenticatedSession_Succeeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, sessionId, orderId, itemId, token) = await SetupOrderInPrepAsync();
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId: null, role: "RestaurantAdmin");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var voidReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel",
            adminToken);
        voidReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{token}\""));
        voidReq.Content = JsonContent.Create(new CancelOrderItemRequest("Admin doğrudan oturumuyla void yaptı"));

        var res = await client.SendAsync(voidReq);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private async Task<int> GetFailedPinAttemptsAsync(Guid tenantId, Guid userId)
    {
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT failed_pin_attempts FROM iam.pin_credentials WHERE tenant_id = @tenantId AND user_id = @userId";
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("userId", userId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task FailedPinAttempt_PersistsEvenIfMainTransactionRollsBack()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, sessionId, orderId, itemId, token) = await SetupOrderInPrepAsync();
        var managerId = await SeedSupervisorAsync(tenantId, branchId, AuthRole.BranchManager, "1234");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        var voidReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel",
            waiterToken);
        voidReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{token}\""));
        voidReq.Content = JsonContent.Create(new CancelOrderItemRequest("Hatalı PIN denemesi", SupervisorPin: "9999", SupervisorUserId: managerId));

        var res = await client.SendAsync(voidReq);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        Assert.Equal(1, await GetFailedPinAttemptsAsync(tenantId, managerId));
    }

    [Fact]
    public async Task SupervisorPin_SuccessfulVerification_ResetsFailedAttemptsCounter()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, sessionId, orderId, itemId, token) = await SetupOrderInPrepAsync();
        var managerId = await SeedSupervisorAsync(tenantId, branchId, AuthRole.BranchManager, "1234");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        // 1. Send 2 failed attempts
        for (int i = 0; i < 2; i++)
        {
            var wrongReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
                HttpMethod.Post,
                $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel",
                waiterToken);
            wrongReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{token}\""));
            wrongReq.Content = JsonContent.Create(new CancelOrderItemRequest("Hatalı deneme", SupervisorPin: "9999", SupervisorUserId: managerId));
            var wrongRes = await client.SendAsync(wrongReq);
            Assert.Equal(HttpStatusCode.Forbidden, wrongRes.StatusCode);
        }

        Assert.Equal(2, await GetFailedPinAttemptsAsync(tenantId, managerId));

        // 2. Send correct PIN
        var correctReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel",
            waiterToken);
        correctReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{token}\""));
        correctReq.Content = JsonContent.Create(new CancelOrderItemRequest("Doğru PIN ile iptal", SupervisorPin: "1234", SupervisorUserId: managerId));
        var correctRes = await client.SendAsync(correctReq);
        Assert.Equal(HttpStatusCode.OK, correctRes.StatusCode);

        Assert.Equal(0, await GetFailedPinAttemptsAsync(tenantId, managerId));
    }

    [Fact]
    public async Task LockedCredential_RejectsEvenCorrectPin()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, sessionId, orderId, itemId, token) = await SetupOrderInPrepAsync();
        var managerId = await SeedSupervisorAsync(tenantId, branchId, AuthRole.BranchManager, "1234");

        // Manually lock credential
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        await using var lockCmd = conn.CreateCommand();
        lockCmd.CommandText = "UPDATE iam.pin_credentials SET failed_pin_attempts = 5, locked_until_utc = NOW() + INTERVAL '1 hour' WHERE tenant_id = @tenantId AND user_id = @userId";
        lockCmd.Parameters.AddWithValue("tenantId", tenantId);
        lockCmd.Parameters.AddWithValue("userId", managerId);
        await lockCmd.ExecuteNonQueryAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        // Attempt void with the CORRECT PIN on locked credential
        var voidReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel",
            waiterToken);
        voidReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{token}\""));
        voidReq.Content = JsonContent.Create(new CancelOrderItemRequest("Kilitliyken doğru PIN", SupervisorPin: "1234", SupervisorUserId: managerId));

        var res = await client.SendAsync(voidReq);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task AuditRecord_NeverContainsCustomerNoteOrPin()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, sessionId, orderId, itemId, token) = await SetupOrderInPrepAsync();
        var managerId = await SeedSupervisorAsync(tenantId, branchId, AuthRole.BranchManager, "4567");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var waiterToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        var voidReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders/{orderId}/items/{itemId}/cancel",
            waiterToken);
        voidReq.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{token}\""));
        voidReq.Content = JsonContent.Create(new CancelOrderItemRequest("Gizli iptal nedeni", SupervisorPin: "4567", SupervisorUserId: managerId));

        var res = await client.SendAsync(voidReq);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString).Options;
        await using var db = new RestaurantOrderDbContext(dbOptions);
        await using var tx = await db.BeginTenantTransactionAsync(tenantId);
        var audits = await db.OrderAuditEntries.Where(a => a.OrderId == new OrderId(orderId)).ToListAsync();

        Assert.NotEmpty(audits);
        var voidAudit = Assert.Single(audits, a => a.Action == "WASTE_CANCEL");
        Assert.Equal(managerId, voidAudit.ActorId);
        Assert.Equal("BranchManager", voidAudit.ActorRole);
        Assert.DoesNotContain("4567", voidAudit.DetailsJson);
        Assert.DoesNotContain("Müşteri acısız istiyor", voidAudit.DetailsJson);
        if (voidAudit.Reason != null) Assert.DoesNotContain("4567", voidAudit.Reason);
    }
}
