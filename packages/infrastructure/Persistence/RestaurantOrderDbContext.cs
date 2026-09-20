using Microsoft.EntityFrameworkCore;

namespace RestaurantOrder.Infrastructure.Persistence;

/// <summary>
/// Primary Entity Framework Core database context for RestaurantOrder.
/// Configured for PostgreSQL with row-level security and tenant isolation support.
/// Business domain entities will be registered in subsequent Phase 2 stages.
/// </summary>
public class RestaurantOrderDbContext : DbContext
{
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
