using System.Net;
using System.Net.Http.Json;
using Npgsql;
using RestaurantOrder.Application.Orders;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Additional negative flow integration tests for customer orders:
/// Missing required modifier, duplicate modifier, quantity out of bounds.
/// </summary>
public partial class CustomerOrderEndpointsIntegrationTests
{
    [Fact]
    public async Task CreateCustomerOrder_MissingRequiredModifier_Returns400BadRequest()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();

        // Add mandatory modifier group (min_selections = 1) assigned to this menu item
        var modGroupId = Guid.NewGuid();
        var modOptionId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.modifier_groups (id, tenant_id, branch_id, name, min_selections, max_selections, is_active, sort_order, created_at, concurrency_token)
                VALUES (@modGroupId, @tenantId, @branchId, 'Sos Seçimi', 1, 2, true, 1, NOW(), gen_random_uuid());

                INSERT INTO tenancy.modifier_options (id, tenant_id, branch_id, modifier_group_id, name, price_delta_minor_units, is_active, sort_order, is_default, created_at, concurrency_token)
                VALUES (@modOptionId, @tenantId, @branchId, @modGroupId, 'Ketçap', 0, true, 1, false, NOW(), gen_random_uuid());

                INSERT INTO tenancy.menu_item_modifier_group_assignments (menu_item_id, modifier_group_id, sort_order)
                VALUES (@menuItemId, @modGroupId, 1);
            ";
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("branchId", branchId);
            cmd.Parameters.AddWithValue("modGroupId", modGroupId);
            cmd.Parameters.AddWithValue("modOptionId", modOptionId);
            cmd.Parameters.AddWithValue("menuItemId", menuItemId);
            await cmd.ExecuteNonQueryAsync();
        }

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        // Draft WITHOUT selecting the required modifier group
        var requestBody = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, null, null, 2500)
        });

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Headers.Add("Idempotency-Key", $"req-mod-key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(requestBody);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errorBody = await res.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_MODIFIER_SELECTION", errorBody);
    }

    [Fact]
    public async Task CreateCustomerOrder_DuplicateModifierOptions_Returns400BadRequest()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();

        var modGroupId = Guid.NewGuid();
        var modOptionId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tenancy.modifier_groups (id, tenant_id, branch_id, name, min_selections, max_selections, is_active, sort_order, created_at, concurrency_token)
                VALUES (@modGroupId, @tenantId, @branchId, 'İçecek Şekeri', 0, 3, true, 1, NOW(), gen_random_uuid());

                INSERT INTO tenancy.modifier_options (id, tenant_id, branch_id, modifier_group_id, name, price_delta_minor_units, is_active, sort_order, is_default, created_at, concurrency_token)
                VALUES (@modOptionId, @tenantId, @branchId, @modGroupId, 'Şeker', 0, true, 1, false, NOW(), gen_random_uuid());

                INSERT INTO tenancy.menu_item_modifier_group_assignments (menu_item_id, modifier_group_id, sort_order)
                VALUES (@menuItemId, @modGroupId, 1);
            ";
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("branchId", branchId);
            cmd.Parameters.AddWithValue("modGroupId", modGroupId);
            cmd.Parameters.AddWithValue("modOptionId", modOptionId);
            cmd.Parameters.AddWithValue("menuItemId", menuItemId);
            await cmd.ExecuteNonQueryAsync();
        }

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        // Sending duplicate modifier option ID
        var requestBody = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, null, new List<CustomerSelectedModifierRequest>
            {
                new(modGroupId, modOptionId),
                new(modGroupId, modOptionId)
            }, 2500)
        });

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Headers.Add("Idempotency-Key", $"dup-mod-key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(requestBody);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errorBody = await res.Content.ReadAsStringAsync();
        Assert.Contains("DUPLICATE_MODIFIER_SELECTION", errorBody);
    }

    [Fact]
    public async Task CreateCustomerOrder_ExceedingQuantityBounds_Returns400BadRequest()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        // Quantity 99 exceeds MaxQuantity (50)
        var requestBody = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 99, null, null, 2500)
        });

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Headers.Add("Idempotency-Key", $"qty-bound-key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(requestBody);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
