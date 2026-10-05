using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public partial class CatalogCrossBranchConstraintIntegrationTests
{
    [Fact]
    public async Task CrossBranch_ModifierOption_To_ModifierGroup_RejectedByForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Option Tenant", $"ot-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchA, "Branch A", $"ba-opt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchB, "Branch B", $"bb-opt-{Guid.NewGuid():N}");

        await using var db = CreateDbContext(tenantId);
        var tId = new TenantId(tenantId);
        var bIdA = new BranchId(branchA);
        var bIdB = new BranchId(branchB);

        var groupInBranchB = ModifierGroup.Create(tId, bIdB, "Sauces B", 0, 2, 0);
        db.ModifierGroups.Add(groupInBranchB);
        await db.SaveChangesAsync();

        // Option in Branch A referencing Group in Branch B directly in DB
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        var insertSql = @"
            INSERT INTO tenancy.modifier_options (id, tenant_id, branch_id, modifier_group_id, name, price_delta_minor_units, is_default, is_active, sort_order, created_at, concurrency_token)
            VALUES (@id, @tenantId, @branchId, @groupId, 'Mayo', 500, false, true, 0, NOW(), @token);";
        await using var cmd = new NpgsqlCommand(insertSql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchA); // Branch A!
        cmd.Parameters.AddWithValue("groupId", groupInBranchB.Id.Value); // Group in Branch B!
        cmd.Parameters.AddWithValue("token", Guid.NewGuid());

        var pgEx = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23503", pgEx.SqlState);
        Assert.Equal("fk_modifier_options_modifier_groups_tenant_branch_group", pgEx.ConstraintName);
    }

    [Fact]
    public async Task CrossBranch_ModifierAssignment_To_MenuItem_RejectedByForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Assign Tenant", $"at-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchA, "Branch A", $"ba-asgn-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchB, "Branch B", $"bb-asgn-{Guid.NewGuid():N}");

        await using var db = CreateDbContext(tenantId);
        var tId = new TenantId(tenantId);
        var bIdA = new BranchId(branchA);
        var bIdB = new BranchId(branchB);

        var menuB = Menu.Create(tId, bIdB, "Menu B", $"mb-asgn-{Guid.NewGuid():N}");
        db.Menus.Add(menuB);
        var catB = MenuCategory.Create(tId, bIdB, menuB.Id, "Cat B", $"cb-asgn-{Guid.NewGuid():N}");
        db.MenuCategories.Add(catB);
        var itemB = MenuItem.Create(tId, bIdB, menuB.Id, catB.Id, "Item B", $"ib-asgn-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(2000));
        db.MenuItems.Add(itemB);

        var groupA = ModifierGroup.Create(tId, bIdA, "Group A", 0, 1, 0);
        db.ModifierGroups.Add(groupA);
        await db.SaveChangesAsync();

        // Assignment in Branch A pointing to Item B (cross-branch)
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        var insertSql = @"
            INSERT INTO tenancy.menu_item_modifier_group_assignments (tenant_id, branch_id, menu_id, menu_item_id, modifier_group_id, sort_order, created_at)
            VALUES (@tenantId, @branchId, @menuId, @itemId, @groupId, 0, NOW());";
        await using var cmd = new NpgsqlCommand(insertSql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchA);
        cmd.Parameters.AddWithValue("menuId", menuB.Id.Value);
        cmd.Parameters.AddWithValue("itemId", itemB.Id.Value); // Item in Branch B
        cmd.Parameters.AddWithValue("groupId", groupA.Id.Value);

        var pgEx = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23503", pgEx.SqlState);
        Assert.Equal("fk_item_modifier_assignments_menu_items_tenant_branch_item", pgEx.ConstraintName);
    }

    [Fact]
    public async Task CrossBranch_ModifierAssignment_To_ModifierGroup_RejectedByForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Assign Group Tenant", $"agt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchA, "Branch A", $"ba-ag-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchB, "Branch B", $"bb-ag-{Guid.NewGuid():N}");

        await using var db = CreateDbContext(tenantId);
        var tId = new TenantId(tenantId);
        var bIdA = new BranchId(branchA);
        var bIdB = new BranchId(branchB);

        var menuA = Menu.Create(tId, bIdA, "Menu A", $"ma-ag-{Guid.NewGuid():N}");
        db.Menus.Add(menuA);
        var catA = MenuCategory.Create(tId, bIdA, menuA.Id, "Cat A", $"ca-ag-{Guid.NewGuid():N}");
        db.MenuCategories.Add(catA);
        var itemA = MenuItem.Create(tId, bIdA, menuA.Id, catA.Id, "Item A", $"ia-ag-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(2000));
        db.MenuItems.Add(itemA);

        var groupB = ModifierGroup.Create(tId, bIdB, "Group B", 0, 1, 0);
        db.ModifierGroups.Add(groupB);
        await db.SaveChangesAsync();

        // Assignment in Branch A pointing to Group B (cross-branch)
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        var insertSql = @"
            INSERT INTO tenancy.menu_item_modifier_group_assignments (tenant_id, branch_id, menu_id, menu_item_id, modifier_group_id, sort_order, created_at)
            VALUES (@tenantId, @branchId, @menuId, @itemId, @groupId, 0, NOW());";
        await using var cmd = new NpgsqlCommand(insertSql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchA);
        cmd.Parameters.AddWithValue("menuId", menuA.Id.Value);
        cmd.Parameters.AddWithValue("itemId", itemA.Id.Value);
        cmd.Parameters.AddWithValue("groupId", groupB.Id.Value); // Group in Branch B

        var pgEx = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23503", pgEx.SqlState);
        Assert.Equal("fk_item_modifier_assignments_groups_tenant_branch_group", pgEx.ConstraintName);
    }

    [Fact]
    public async Task CrossBranch_BranchItemAvailability_To_ItemVariant_RejectedByForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Var Avail Tenant", $"vat-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchA, "Branch A", $"ba-va-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchB, "Branch B", $"bb-va-{Guid.NewGuid():N}");

        await using var db = CreateDbContext(tenantId);
        var tId = new TenantId(tenantId);
        var bIdA = new BranchId(branchA);
        var bIdB = new BranchId(branchB);

        var menuA = Menu.Create(tId, bIdA, "Menu A", $"ma-va-{Guid.NewGuid():N}");
        db.Menus.Add(menuA);
        var catA = MenuCategory.Create(tId, bIdA, menuA.Id, "Cat A", $"ca-va-{Guid.NewGuid():N}");
        db.MenuCategories.Add(catA);
        var itemA = MenuItem.Create(tId, bIdA, menuA.Id, catA.Id, "Item A", $"ia-va-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(2000));
        db.MenuItems.Add(itemA);

        var menuB = Menu.Create(tId, bIdB, "Menu B", $"mb-va-{Guid.NewGuid():N}");
        db.Menus.Add(menuB);
        var catB = MenuCategory.Create(tId, bIdB, menuB.Id, "Cat B", $"cb-va-{Guid.NewGuid():N}");
        db.MenuCategories.Add(catB);
        var itemB = MenuItem.Create(tId, bIdB, menuB.Id, catB.Id, "Item B", $"ib-va-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(2000));
        db.MenuItems.Add(itemB);
        var varB = ItemVariant.Create(tId, bIdB, menuB.Id, itemB.Id, "Var B", "VB", PriceAmount.FromMinorUnits(2500));
        db.ItemVariants.Add(varB);
        await db.SaveChangesAsync();

        // Availability in Branch A pointing to Item A (in Branch A) but Variant in Branch B
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        var insertSql = @"
            INSERT INTO tenancy.branch_item_availabilities (id, tenant_id, branch_id, menu_item_id, item_variant_id, is_available, reason_code, note, changed_by_user_id, changed_at, concurrency_token)
            VALUES (@id, @tenantId, @branchId, @itemId, @varId, false, 86, 'Sold out', @userId, NOW(), @token);";
        await using var cmd = new NpgsqlCommand(insertSql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchA); // Branch A!
        cmd.Parameters.AddWithValue("itemId", itemA.Id.Value); // Item in Branch A
        cmd.Parameters.AddWithValue("varId", varB.Id.Value); // Variant in Branch B!
        cmd.Parameters.AddWithValue("userId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("token", Guid.NewGuid());

        var pgEx = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23503", pgEx.SqlState);
        Assert.Equal("fk_branch_item_availabilities_variants_tenant_branch_variant", pgEx.ConstraintName);
    }

    [Fact]
    public async Task CrossTenant_CatalogEntity_RejectedByForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantA, "Tenant A", $"ta-ct-{Guid.NewGuid():N}");
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantB, "Tenant B", $"tb-ct-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantA, branchA, "Branch A", $"ba-ct-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantB, branchB, "Branch B", $"bb-ct-{Guid.NewGuid():N}");

        await using var dbB = CreateDbContext(tenantB);
        var tIdB = new TenantId(tenantB);
        var bIdB = new BranchId(branchB);
        var menuB = Menu.Create(tIdB, bIdB, "Menu B", $"mb-ct-{Guid.NewGuid():N}");
        dbB.Menus.Add(menuB);
        await dbB.SaveChangesAsync();

        // Direct SQL insert for Category with Tenant A pointing to Menu of Tenant B
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        var sql = @"
            INSERT INTO tenancy.menu_categories (id, tenant_id, branch_id, menu_id, name, slug, description, sort_order, is_active, created_at, concurrency_token)
            VALUES (@id, @tenantA, @branchA, @menuBId, 'Cross Tenant Cat', 'ct-cat', 'Desc', 0, true, NOW(), @token);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tenantA", tenantA);
        cmd.Parameters.AddWithValue("branchA", branchA);
        cmd.Parameters.AddWithValue("menuBId", menuB.Id.Value);
        cmd.Parameters.AddWithValue("token", Guid.NewGuid());

        var pgEx = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23503", pgEx.SqlState);
    }

    [Fact]
    public async Task DeletePreparationStation_InUseByMenuItem_RestrictedByForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Del Station Tenant", $"dst-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Branch", $"dsb-{Guid.NewGuid():N}");
        await SeedPreparationStationAsync(tenantId, branchId, stationId, "Grill", PreparationStationType.Kitchen);

        await using var db = CreateDbContext(tenantId);
        var tId = new TenantId(tenantId);
        var bId = new BranchId(branchId);
        var menu = Menu.Create(tId, bId, "Menu", $"dsm-{Guid.NewGuid():N}");
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tId, bId, menu.Id, "Cat", $"dsc-{Guid.NewGuid():N}");
        db.MenuCategories.Add(cat);
        var item = MenuItem.Create(
            tId, bId, menu.Id, cat.Id, "Burger", $"dsi-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(1500),
            preparationStationId: new PreparationStationId(stationId));
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();

        // Attempting to physically delete the preparation station must be blocked by Restrict FK
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        var deleteSql = "DELETE FROM tenancy.preparation_stations WHERE id = @id AND tenant_id = @tenantId AND branch_id = @branchId;";
        await using var cmd = new NpgsqlCommand(deleteSql, conn);
        cmd.Parameters.AddWithValue("id", stationId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchId);

        var pgEx = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23503", pgEx.SqlState);
        Assert.Equal("fk_menu_items_prep_stations_tenant_branch_station", pgEx.ConstraintName);
    }
}
