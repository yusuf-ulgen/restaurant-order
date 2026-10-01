namespace RestaurantOrder.Infrastructure.Persistence.Seed;

/// <summary>
/// Contract for seeding synthetic data into local development databases.
/// STRICT INVARIANT: Must NEVER run in Staging or Production.
/// </summary>
public interface IDevDataSeeder
{
    Task<DevSeedResult> SeedAsync(CancellationToken cancellationToken = default);
}

public sealed record DevSeedResult(
    int TenantsCreated,
    int BrandsCreated,
    int BranchesCreated,
    string Message);
