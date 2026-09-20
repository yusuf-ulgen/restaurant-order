using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence;

/// <summary>
/// Primary Entity Framework Core database context for RestaurantOrder.
/// Configured for PostgreSQL with row-level security and tenant isolation support.
/// Includes Global Query Filters as a secondary defense-in-depth layer.
/// </summary>
public class RestaurantOrderDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Branch> Branches => Set<Branch>();

    public RestaurantOrderDbContext(
        DbContextOptions<RestaurantOrderDbContext> options,
        ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext ?? TenantContext.Empty;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply entity configurations from the current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RestaurantOrderDbContext).Assembly);

        // Secondary defense-in-depth: EF Core Global Query Filters
        // Note: PostgreSQL Row-Level Security (RLS) remains the definitive security boundary.
        // Even if IgnoreQueryFilters() is called, PostgreSQL RLS prevents cross-tenant access.
        modelBuilder.Entity<Tenant>().HasQueryFilter(t =>
            _tenantContext.HasTenant && t.Id == new TenantId(_tenantContext.TenantId!.Value));

        modelBuilder.Entity<Brand>().HasQueryFilter(b =>
            _tenantContext.HasTenant && b.TenantId == new TenantId(_tenantContext.TenantId!.Value));

        modelBuilder.Entity<Branch>().HasQueryFilter(br =>
            _tenantContext.HasTenant && br.TenantId == new TenantId(_tenantContext.TenantId!.Value));
    }

    /// <summary>
    /// Binds the active PostgreSQL transaction or connection to the specified tenant context.
    /// Uses transaction-local 'set_config(..., is_local => true)' so the context does not leak across pooled connections.
    /// </summary>
    public async Task SetTenantSessionAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var conn = Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";

        var param = cmd.CreateParameter();
        param.ParameterName = "tenantId";
        param.Value = tenantId.ToString();
        cmd.Parameters.Add(param);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Clears the PostgreSQL session variable 'app.current_tenant_id' to prevent connection pool leakage.
    /// </summary>
    public async Task ClearTenantSessionAsync(CancellationToken cancellationToken = default)
    {
        var conn = Database.GetDbConnection();
        if (conn.State == ConnectionState.Open)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', '', false);";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
