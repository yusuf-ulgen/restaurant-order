using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence;

/// <summary>
/// Primary Entity Framework Core database context for RestaurantOrder.
/// Configured for PostgreSQL with row-level security and tenant isolation support.
/// </summary>
public class RestaurantOrderDbContext : DbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Branch> Branches => Set<Branch>();

    public RestaurantOrderDbContext(DbContextOptions<RestaurantOrderDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply entity configurations from the current assembly as they are created
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RestaurantOrderDbContext).Assembly);
    }
}
