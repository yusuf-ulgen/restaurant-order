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

public partial class CatalogCrossBranchConstraintIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public CatalogCrossBranchConstraintIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private static void AssertPostgresForeignKeyViolation(DbUpdateException ex, string? expectedConstraintName = null)
    {
        var pgEx = ex.InnerException as PostgresException ?? ex.GetBaseException() as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState); // foreign_key_violation
        if (!string.IsNullOrEmpty(expectedConstraintName))
        {
            Assert.Equal(expectedConstraintName, pgEx.ConstraintName);
        }
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

    private RestaurantOrderDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;

        return new RestaurantOrderDbContext(options, new TenantContext(tenantId, isAuthenticated: true));
    }

    private async Task SeedBranchAsync(Guid tenantId, Guid branchId, string name, string slug)
    {
        var brandId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        var brandSql = @"
            INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
            VALUES (@id, @tenantId, @name, @slug, 'Active', NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmd = new NpgsqlCommand(brandSql, conn))
        {
            cmd.Parameters.AddWithValue("id", brandId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("name", $"{name} Brand");
            cmd.Parameters.AddWithValue("slug", $"brand-{slug}");
            cmd.Parameters.AddWithValue("token", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }

        var branchSql = @"
            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
            VALUES (@id, @tenantId, @brandId, @name, @slug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using (var cmd = new NpgsqlCommand(branchSql, conn))
        {
            cmd.Parameters.AddWithValue("id", branchId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("brandId", brandId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("slug", slug);
            cmd.Parameters.AddWithValue("token", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedPreparationStationAsync(Guid tenantId, Guid branchId, Guid stationId, string displayName, PreparationStationType stationType)
    {
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        var sql = @"
            INSERT INTO tenancy.preparation_stations (id, tenant_id, branch_id, code, display_name, station_type, is_active, sort_order, created_at, concurrency_token)
            VALUES (@id, @tenantId, @branchId, @code, @displayName, @stationType, true, 1, NOW(), @token)
            ON CONFLICT (id) DO NOTHING;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", stationId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("code", $"station-{stationId:N}");
        cmd.Parameters.AddWithValue("displayName", displayName);
        cmd.Parameters.AddWithValue("stationType", (int)stationType);
        cmd.Parameters.AddWithValue("token", Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SameBranch_CatalogEntities_PersistSuccessfully()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Valid Tenant", $"vt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Valid Branch", $"vb-{Guid.NewGuid():N}");

        await using var db = CreateDbContext(tenantId);
        var tId = new TenantId(tenantId);
        var bId = new BranchId(branchId);

        var menu = Menu.Create(tId, bId, "Summer Menu", $"summer-{Guid.NewGuid():N}", "Description");
        db.Menus.Add(menu);

        var category = MenuCategory.Create(tId, bId, menu.Id, "Drinks", $"drinks-{Guid.NewGuid():N}", "Refreshing");
        db.MenuCategories.Add(category);

        var item = MenuItem.Create(tId, bId, menu.Id, category.Id, "Lemonade", $"lemonade-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(5000));
        db.MenuItems.Add(item);

        var variant = ItemVariant.Create(tId, bId, menu.Id, item.Id, "Large", "LRG", PriceAmount.FromMinorUnits(6500), 1, isDefault: false);
        db.ItemVariants.Add(variant);

        var group = ModifierGroup.Create(tId, bId, "Ice Options", 0, 1, 0);
        group.AddOption("No Ice", PriceAmount.Zero);
        db.ModifierGroups.Add(group);

        var assignment = MenuItemModifierGroupAssignment.Create(tId, bId, menu.Id, item.Id, group.Id, 0);
        db.MenuItemModifierGroupAssignments.Add(assignment);

        var avail = BranchItemAvailability.CreateItemUnavailable(tId, bId, item.Id, AvailabilityReasonCode.SoldOut, "Out of lemons", null, UserId.New());
        db.BranchItemAvailabilities.Add(avail);

        await db.SaveChangesAsync();

        Assert.NotNull(await db.MenuItems.FirstOrDefaultAsync(i => i.Id == item.Id));
    }

    [Fact]
    public async Task CrossBranch_MenuItem_To_PreparationStation_RejectedByForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Station Tenant", $"st-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchA, "Branch A", $"ba-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchB, "Branch B", $"bb-{Guid.NewGuid():N}");

        var stationInBranchB = Guid.NewGuid();
        await SeedPreparationStationAsync(tenantId, branchB, stationInBranchB, "Kitchen B", PreparationStationType.Kitchen);

        await using var db = CreateDbContext(tenantId);
        var tId = new TenantId(tenantId);
        var bIdA = new BranchId(branchA);

        var menu = Menu.Create(tId, bIdA, "Menu A", $"ma-{Guid.NewGuid():N}");
        db.Menus.Add(menu);
        var cat = MenuCategory.Create(tId, bIdA, menu.Id, "Cat A", $"ca-{Guid.NewGuid():N}");
        db.MenuCategories.Add(cat);

        // MenuItem in Branch A pointing to Station in Branch B
        var item = MenuItem.Create(
            tId, bIdA, menu.Id, cat.Id, "Item A", $"ia-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(1000),
            preparationStationId: new PreparationStationId(stationInBranchB));
        db.MenuItems.Add(item);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertPostgresForeignKeyViolation(ex, "fk_menu_items_prep_stations_tenant_branch_station");
    }

    [Fact]
    public async Task CrossBranch_MenuCategory_To_Menu_RejectedByForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Menu Tenant", $"mt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchA, "Branch A", $"ba2-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchB, "Branch B", $"bb2-{Guid.NewGuid():N}");

        await using var db = CreateDbContext(tenantId);
        var tId = new TenantId(tenantId);
        var bIdA = new BranchId(branchA);
        var bIdB = new BranchId(branchB);

        var menuInBranchB = Menu.Create(tId, bIdB, "Menu B", $"mb-{Guid.NewGuid():N}");
        db.Menus.Add(menuInBranchB);
        await db.SaveChangesAsync();

        // Category in Branch A pointing to Menu in Branch B
        var catInBranchA = MenuCategory.Create(tId, bIdA, menuInBranchB.Id, "Cat A", $"ca2-{Guid.NewGuid():N}");
        db.MenuCategories.Add(catInBranchA);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertPostgresForeignKeyViolation(ex, "fk_menu_categories_menus_tenant_branch_menu");
    }

    [Fact]
    public async Task CrossBranch_ItemVariant_To_MenuItem_RejectedByForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Var Tenant", $"vt2-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchA, "Branch A", $"ba3-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchB, "Branch B", $"bb3-{Guid.NewGuid():N}");

        await using var db = CreateDbContext(tenantId);
        var tId = new TenantId(tenantId);
        var bIdA = new BranchId(branchA);
        var bIdB = new BranchId(branchB);

        var menuB = Menu.Create(tId, bIdB, "Menu B", $"mb3-{Guid.NewGuid():N}");
        db.Menus.Add(menuB);
        var catB = MenuCategory.Create(tId, bIdB, menuB.Id, "Cat B", $"cb3-{Guid.NewGuid():N}");
        db.MenuCategories.Add(catB);
        var itemInBranchB = MenuItem.Create(tId, bIdB, menuB.Id, catB.Id, "Item B", $"ib3-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(2000));
        db.MenuItems.Add(itemInBranchB);
        await db.SaveChangesAsync();

        // Variant in Branch A pointing to Item in Branch B
        var variantInBranchA = ItemVariant.Create(tId, bIdA, menuB.Id, itemInBranchB.Id, "Variant A", "VA", PriceAmount.FromMinorUnits(2500));
        db.ItemVariants.Add(variantInBranchA);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertPostgresForeignKeyViolation(ex, "fk_item_variants_menu_items_tenant_branch_item");
    }

    [Fact]
    public async Task CrossBranch_BranchItemAvailability_To_MenuItem_RejectedByForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Avail Tenant", $"at2-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchA, "Branch A", $"ba4-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchB, "Branch B", $"bb4-{Guid.NewGuid():N}");

        await using var db = CreateDbContext(tenantId);
        var tId = new TenantId(tenantId);
        var bIdA = new BranchId(branchA);
        var bIdB = new BranchId(branchB);

        var menuB = Menu.Create(tId, bIdB, "Menu B", $"mb4-{Guid.NewGuid():N}");
        db.Menus.Add(menuB);
        var catB = MenuCategory.Create(tId, bIdB, menuB.Id, "Cat B", $"cb4-{Guid.NewGuid():N}");
        db.MenuCategories.Add(catB);
        var itemInBranchB = MenuItem.Create(tId, bIdB, menuB.Id, catB.Id, "Item B", $"ib4-{Guid.NewGuid():N}", PriceAmount.FromMinorUnits(2000));
        db.MenuItems.Add(itemInBranchB);
        await db.SaveChangesAsync();

        // Availability in Branch A pointing to Item in Branch B
        var availInBranchA = BranchItemAvailability.CreateItemUnavailable(tId, bIdA, itemInBranchB.Id, AvailabilityReasonCode.KitchenCapacity, "Busy", null, UserId.New());
        db.BranchItemAvailabilities.Add(availInBranchA);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertPostgresForeignKeyViolation(ex, "fk_branch_item_availabilities_menu_items_tenant_branch_item");
    }
}
