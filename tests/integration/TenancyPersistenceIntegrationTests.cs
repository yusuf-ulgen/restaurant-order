using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying PostgreSQL 16 schema creation, EF Core mappings,
/// constraints, composite foreign keys, and idempotency using Testcontainers.
/// </summary>
public class TenancyPersistenceIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public TenancyPersistenceIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private RestaurantOrderDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(RestaurantOrderDbContext).Assembly.FullName);
            })
            .Options;

        return new RestaurantOrderDbContext(options);
    }

    [Fact]
    public async Task Migration_Applies_Cleanly_To_Fresh_Database_And_Creates_Schemas_And_Tables()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await connection.OpenAsync();

        // 1. Verify schema tenancy exists
        await using var schemaCmd = connection.CreateCommand();
        schemaCmd.CommandText = "SELECT 1 FROM pg_namespace WHERE nspname = 'tenancy';";
        var schemaExists = await schemaCmd.ExecuteScalarAsync();
        Assert.NotNull(schemaExists);

        // 2. Verify tables exist in schema tenancy
        var expectedTables = new[] { "tenants", "brands", "branches" };
        foreach (var tableName in expectedTables)
        {
            await using var tableCmd = connection.CreateCommand();
            tableCmd.CommandText = "SELECT 1 FROM information_schema.tables WHERE table_schema = 'tenancy' AND table_name = @name;";
            tableCmd.Parameters.AddWithValue("name", tableName);
            var tableExists = await tableCmd.ExecuteScalarAsync();
            Assert.NotNull(tableExists);
        }
    }

    [Fact]
    public async Task Migration_Is_Idempotent_On_Second_Application()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        await using var context = CreateDbContext();
        // Second migration call must execute cleanly without exceptions
        var exception = await Record.ExceptionAsync(() => context.Database.MigrateAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task Tenant_Slug_Uniqueness_Constraint_Enforced()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();

        var slug = $"slug-t-{Guid.NewGuid():N}";
        var tenant1 = Tenant.Create("Tenant One", slug);
        var tenant2 = Tenant.Create("Tenant Two", slug);

        context.Tenants.Add(tenant1);
        await context.SaveChangesAsync();

        context.Tenants.Add(tenant2);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.IsType<PostgresException>(ex.InnerException);
        var pgEx = (PostgresException)ex.InnerException!;
        Assert.Equal("23505", pgEx.SqlState); // unique_violation
    }

    [Fact]
    public async Task Brand_Slug_Uniqueness_Within_Tenant_Enforced()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();

        var tenantA = Tenant.Create("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantB = Tenant.Create("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        context.Tenants.AddRange(tenantA, tenantB);
        await context.SaveChangesAsync();

        var sharedBrandSlug = "prime-burger";
        var brandA1 = Brand.Create(tenantA.Id, "Brand A1", sharedBrandSlug);
        var brandA2 = Brand.Create(tenantA.Id, "Brand A2", sharedBrandSlug);
        var brandB1 = Brand.Create(tenantB.Id, "Brand B1", sharedBrandSlug);

        // 1. Adding Brand A1 succeeds
        context.Brands.Add(brandA1);
        await context.SaveChangesAsync();

        // 2. Adding Brand B1 with same slug in Tenant B succeeds (tenant isolation)
        context.Brands.Add(brandB1);
        await context.SaveChangesAsync();

        // 3. Adding Brand A2 with duplicate slug in Tenant A throws unique violation
        context.Brands.Add(brandA2);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.IsType<PostgresException>(ex.InnerException);
        var pgEx = (PostgresException)ex.InnerException!;
        Assert.Equal("23505", pgEx.SqlState);
    }

    [Fact]
    public async Task Branch_Slug_Uniqueness_Within_Tenant_Enforced()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();

        var tenant = Tenant.Create("Tenant Branch Test", $"tenant-br-{Guid.NewGuid():N}");
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var brand = Brand.Create(tenant.Id, "Brand For Branch", $"brand-{Guid.NewGuid():N}");
        context.Brands.Add(brand);
        await context.SaveChangesAsync();

        var branchSlug = "kadikoy";
        var branch1 = Branch.Create(tenant.Id, brand.Id, "Kadikoy Branch", branchSlug);
        var branch2 = Branch.Create(tenant.Id, brand.Id, "Kadikoy Duplicate", branchSlug);

        context.Branches.Add(branch1);
        await context.SaveChangesAsync();

        context.Branches.Add(branch2);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.IsType<PostgresException>(ex.InnerException);
        var pgEx = (PostgresException)ex.InnerException!;
        Assert.Equal("23505", pgEx.SqlState);
    }

    [Fact]
    public async Task Composite_ForeignKey_Rejects_CrossTenant_Brand_Branch_Relationship()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();

        var tenantA = Tenant.Create("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantB = Tenant.Create("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        context.Tenants.AddRange(tenantA, tenantB);
        await context.SaveChangesAsync();

        var brandB = Brand.Create(tenantB.Id, "Brand B", $"brand-b-{Guid.NewGuid():N}");
        context.Brands.Add(brandB);
        await context.SaveChangesAsync();

        // Attempt to create a branch belonging to Tenant A, but referencing Brand B (which belongs to Tenant B)
        // Note: Domain entity Branch.Create checks consistency, so we use direct reflection or raw SQL to test DB constraint
        await using var connection = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await connection.OpenAsync();

        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
            VALUES (@id, @tenantId, @brandId, 'Cross Tenant Branch', @slug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), @token);";

        insertCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        insertCmd.Parameters.AddWithValue("tenantId", tenantA.Id.Value); // Tenant A
        insertCmd.Parameters.AddWithValue("brandId", brandB.Id.Value);   // Brand B belongs to Tenant B!
        insertCmd.Parameters.AddWithValue("slug", $"cross-{Guid.NewGuid():N}");
        insertCmd.Parameters.AddWithValue("token", Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<PostgresException>(() => insertCmd.ExecuteNonQueryAsync());
        Assert.Equal("23503", ex.SqlState); // foreign_key_violation
        Assert.Contains("fk_branches_brands_tenant_id_brand_id", ex.ConstraintName);
    }

    [Fact]
    public async Task DeleteBehavior_Restricts_Tenant_Deletion_When_Child_Records_Exist()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();

        var tenant = Tenant.Create("Tenant Restrict Test", $"tenant-res-{Guid.NewGuid():N}");
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var brand = Brand.Create(tenant.Id, "Brand Child", $"brand-res-{Guid.NewGuid():N}");
        context.Brands.Add(brand);
        await context.SaveChangesAsync();

        // Attempting to delete tenant while it has child brand records must be restricted
        context.Tenants.Remove(tenant);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.IsType<PostgresException>(ex.InnerException);
        var pgEx = (PostgresException)ex.InnerException!;
        Assert.Equal("23503", pgEx.SqlState); // foreign_key_violation due to Restrict
    }

    [Fact]
    public async Task Utc_Timestamps_And_Status_CheckConstraints_Enforced()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();

        var tenant = Tenant.Create("Tenant UTC", $"tenant-utc-{Guid.NewGuid():N}");
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        // Verify UTC timestamp roundtrip
        var loaded = await context.Tenants.FirstAsync(t => t.Id == tenant.Id);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);

        // Verify invalid status check constraint rejection via raw SQL
        await using var connection = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await connection.OpenAsync();

        await using var badStatusCmd = connection.CreateCommand();
        badStatusCmd.CommandText = @"
            INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
            VALUES (@id, 'Bad Tenant', @slug, 'INVALID_STATUS', NOW(), @token);";

        badStatusCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        badStatusCmd.Parameters.AddWithValue("slug", $"bad-{Guid.NewGuid():N}");
        badStatusCmd.Parameters.AddWithValue("token", Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<PostgresException>(() => badStatusCmd.ExecuteNonQueryAsync());
        Assert.Equal("23514", ex.SqlState); // check_violation
        Assert.Contains("ck_tenants_status", ex.ConstraintName);
    }
}
