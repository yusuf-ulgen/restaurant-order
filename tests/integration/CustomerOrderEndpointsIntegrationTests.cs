using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests for Phase 8.3 Customer Order endpoints.
/// Verifies authoritative pricing, idempotency, snapshot fidelity,
/// audit redaction, and session authorization.
/// </summary>
public partial class CustomerOrderEndpointsIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CustomerOrderEndpointsIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, Guid tableId, Guid sessionId, Guid menuItemId)> SetupSessionAndCatalogAsync()
    {
        var tenantId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var menuId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var menuItemId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
            VALUES (@tenantId, 'Order Test Tenant', @tenantSlug, 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
            VALUES (@brandId, @tenantId, 'Order Brand', @brandSlug, 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
            VALUES (@branchId, @tenantId, @brandId, 'Order Branch', @branchSlug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid());

            INSERT INTO tenancy.branch_settings (id, tenant_id, branch_id, timezone, currency, default_locale, supported_locales, prices_include_tax, default_tax_rate_bps, is_service_charge_enabled, service_charge_rate_bps, is_order_taking_enabled, created_at, concurrency_token)
            VALUES (gen_random_uuid(), @tenantId, @branchId, 'Europe/Istanbul', 'TRY', 'tr-TR', '[""tr-TR""]', true, 0, false, 0, true, NOW(), gen_random_uuid());

            INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
            VALUES (@areaId, @tenantId, @branchId, 'Main Hall', @areaSlug, 1, 1, true, NOW(), gen_random_uuid());

            INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, public_code, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
            VALUES (@tableId, @tenantId, @branchId, @areaId, 'T-01', 'Table 1', md5(random()::text), 4, 100, 100, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW());

            INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, activated_at_utc, created_at_utc, concurrency_token)
            VALUES (@sessionId, @tenantId, @branchId, @tableId, 2, 2, NOW(), NOW(), NOW(), gen_random_uuid());

            INSERT INTO tenancy.menus (id, tenant_id, branch_id, name, slug, status, sort_order, created_at, concurrency_token)
            VALUES (@menuId, @tenantId, @branchId, 'Main Menu', @menuSlug, 2, 1, NOW(), gen_random_uuid());

            INSERT INTO tenancy.menu_categories (id, tenant_id, branch_id, menu_id, name, slug, sort_order, is_active, created_at, concurrency_token)
            VALUES (@categoryId, @tenantId, @branchId, @menuId, 'Burgers', @catSlug, 1, true, NOW(), gen_random_uuid());

            INSERT INTO tenancy.menu_items (id, tenant_id, branch_id, menu_id, category_id, name, slug, base_price_minor_units, sort_order, is_active, created_at, concurrency_token)
            VALUES (@menuItemId, @tenantId, @branchId, @menuId, @categoryId, 'Classic Burger', @itemSlug, 2500, 1, true, NOW(), gen_random_uuid());
        ";

        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("tenantSlug", $"t-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("brandId", brandId);
        cmd.Parameters.AddWithValue("brandSlug", $"b-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("branchSlug", $"br-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("areaId", areaId);
        cmd.Parameters.AddWithValue("areaSlug", $"a-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("tableId", tableId);
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        cmd.Parameters.AddWithValue("menuId", menuId);
        cmd.Parameters.AddWithValue("menuSlug", $"m-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("categoryId", categoryId);
        cmd.Parameters.AddWithValue("catSlug", $"c-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("menuItemId", menuItemId);
        cmd.Parameters.AddWithValue("itemSlug", $"i-{Guid.NewGuid():N}");

        await cmd.ExecuteNonQueryAsync();
        return (tenantId, branchId, tableId, sessionId, menuItemId);
    }

    [Fact]
    public async Task CreateCustomerOrder_Successful_And_GetEndpoints_ReturnSnapshotAndEtag()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, tableId, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var requestBody = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 2, "Acısız ve soğansız lütfen", null, 2500)
        });

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Headers.Add("Idempotency-Key", $"ord-key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(requestBody);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.True(res.Headers.Contains("ETag"));

        var created = await res.Content.ReadFromJsonAsync<CustomerOrderResponseDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(sessionId, created.DiningSessionId);
        Assert.Equal(tableId, created.TableId);
        Assert.Equal(1, created.RoundNumber);
        Assert.Equal("Submitted", created.Status);
        Assert.Equal(5000, created.SubtotalMinorUnits);
        Assert.Equal(5000, created.TotalMinorUnits);
        Assert.Single(created.Items);
        Assert.Equal("Classic Burger", created.Items[0].ItemName);
        Assert.Equal(2, created.Items[0].Quantity);
        Assert.Equal(2500, created.Items[0].UnitPriceMinorUnits);
        Assert.Equal(5000, created.Items[0].LineTotalMinorUnits);
        Assert.Equal("Acısız ve soğansız lütfen", created.Items[0].CustomerNote);

        // Security Audit Check: Customer note MUST NOT be stored in audit logs
        var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        await using var auditDb = new RestaurantOrderDbContext(dbOptions);
        var audit = await auditDb.Set<OrderAuditEntry>()
            .IgnoreQueryFilters()
            .Where(a => a.OrderId == new OrderId(created.Id))
            .FirstOrDefaultAsync();

        Assert.NotNull(audit);
        Assert.DoesNotContain("Acısız ve soğansız lütfen", audit.DetailsJson);
        Assert.Contains("\"hasNotes\":true", audit.DetailsJson);

        // GET /api/v1/customer/orders
        var listReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, "/api/v1/customer/orders", customerToken);
        var listRes = await client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await listRes.Content.ReadFromJsonAsync<List<CustomerOrderResponseDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal(created.Id, list[0].Id);

        // GET /api/v1/customer/orders/{id}
        var getReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/customer/orders/{created.Id}", customerToken);
        var getRes = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        Assert.True(getRes.Headers.Contains("ETag"));
        var single = await getRes.Content.ReadFromJsonAsync<CustomerOrderResponseDto>(JsonOptions);
        Assert.NotNull(single);
        Assert.Equal(created.Id, single.Id);
    }

    [Fact]
    public async Task CreateCustomerOrder_MissingCsrfToken_Returns403Forbidden()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var requestBody = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, null, null, 2500)
        });

        // Request has ambient cookie but missing X-CSRF-Token header
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customer/orders");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", customerToken);
        req.Headers.Add("Cookie", $"{AuthCookieService.CsrfTokenCookieName}=some_valid_cookie_value");
        req.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(requestBody);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task CreateCustomerOrder_MissingIdempotencyKey_Returns400BadRequest()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var requestBody = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, null, null, 2500)
        });

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Content = JsonContent.Create(requestBody);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task CreateCustomerOrder_DuplicateIdempotencyKey_SamePayload_ReplaysSameOrder()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var key = $"idem-key-{Guid.NewGuid():N}";

        var requestBody = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, "Tek burger", null, 2500)
        });

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req1.Headers.Add("Idempotency-Key", key);
        req1.Content = JsonContent.Create(requestBody);
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var dto1 = await res1.Content.ReadFromJsonAsync<CustomerOrderResponseDto>(JsonOptions);

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req2.Headers.Add("Idempotency-Key", key);
        req2.Content = JsonContent.Create(requestBody);
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var dto2 = await res2.Content.ReadFromJsonAsync<CustomerOrderResponseDto>(JsonOptions);

        Assert.Equal(dto1!.Id, dto2!.Id);
        Assert.Equal(dto1.RoundNumber, dto2.RoundNumber);
    }

    [Fact]
    public async Task CreateCustomerOrder_SameKey_DifferentPayload_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var key = $"conflict-key-{Guid.NewGuid():N}";

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req1.Headers.Add("Idempotency-Key", key);
        req1.Content = JsonContent.Create(new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, null, null, 2500)
        }));
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Same key, but different quantity (2 instead of 1)
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req2.Headers.Add("Idempotency-Key", key);
        req2.Content = JsonContent.Create(new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 2, null, null, 2500)
        }));
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
        var errorBody = await res2.Content.ReadAsStringAsync();
        Assert.Contains("IDEMPOTENCY_CONFLICT", errorBody);
    }

    [Fact]
    public async Task CreateCustomerOrder_PriceChanged_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        // Catalog has price 2500, customer expects 1900
        var requestBody = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, null, null, 1900)
        });

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(requestBody);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var errorBody = await res.Content.ReadAsStringAsync();
        Assert.Contains("ORDER_PRICE_CHANGED", errorBody);
    }

    [Fact]
    public async Task CreateCustomerOrder_Item86Stockout_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, tableId, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();

        // 86 the item via branch_item_availabilities
        await using (var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.branch_item_availabilities (id, tenant_id, branch_id, menu_item_id, item_variant_id, is_available, reason_code, note, changed_by_user_id, changed_at, concurrency_token)
                VALUES (gen_random_uuid(), @tenantId, @branchId, @menuItemId, NULL, false, 1, '86 out of stock', gen_random_uuid(), NOW(), gen_random_uuid());";
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("branchId", branchId);
            cmd.Parameters.AddWithValue("menuItemId", menuItemId);
            await cmd.ExecuteNonQueryAsync();
        }

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, null, null, 2500)
        }));

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var errorBody = await res.Content.ReadAsStringAsync();
        Assert.Contains("ITEM_OUT_OF_STOCK", errorBody);
    }

    [Fact]
    public async Task CreateCustomerOrder_ClosedOrBillRequestedSession_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();

        // Set session to BillRequested (status = 3)
        await using (var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE tenancy.dining_sessions SET status = 3, bill_requested_at_utc = NOW() WHERE id = @id";
            cmd.Parameters.AddWithValue("id", sessionId);
            await cmd.ExecuteNonQueryAsync();
        }

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, null, null, 2500)
        }));

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var errorBody = await res.Content.ReadAsStringAsync();
        Assert.Contains("SESSION_NOT_ACTIVE", errorBody);
    }

    [Fact]
    public async Task GetCustomerOrder_ForeignSessionToken_Returns404NotFound()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, tableId, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, null, null, 2500)
        }));
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Content.ReadFromJsonAsync<CustomerOrderResponseDto>(JsonOptions);

        // Another session token (must exist in DB as active session so it passes session auth middleware)
        var foreignSessionId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, activated_at_utc, concurrency_token)
                VALUES (@sId, @tenantId, @branchId, @tableId, 2, 2, NOW(), NOW(), gen_random_uuid());";
            cmd.Parameters.AddWithValue("sId", foreignSessionId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("branchId", branchId);
            cmd.Parameters.AddWithValue("tableId", tableId);
            await cmd.ExecuteNonQueryAsync();
        }
        var foreignToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, foreignSessionId);

        var foreignReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/customer/orders/{created!.Id}", foreignToken);
        var foreignRes = await client.SendAsync(foreignReq);
        Assert.Equal(HttpStatusCode.NotFound, foreignRes.StatusCode);
    }
}
