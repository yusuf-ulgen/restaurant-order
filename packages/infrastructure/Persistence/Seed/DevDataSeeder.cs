using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent synthetic data seeder for local development only.
/// Invariants:
/// 1. Must never execute in Staging or Production.
/// 2. Must never use real customer data (PII).
/// 3. Must be idempotent (safe to run multiple times without duplicating or failing).
/// 4. Completely decoupled from database migrations.
/// </summary>
public class DevDataSeeder : IDevDataSeeder
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DevDataSeeder> _logger;

    public static readonly TenantId SyntheticTenant1Id = TenantId.From(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    public static readonly TenantId SyntheticTenant2Id = TenantId.From(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    public static readonly BrandId SyntheticBrand1Id = BrandId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
    public static readonly BrandId SyntheticBrand2Id = BrandId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
    public static readonly BrandId SyntheticBrand3Id = BrandId.From(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

    public static readonly BranchId SyntheticBranch1Id = BranchId.From(Guid.Parse("d1111111-1111-1111-1111-111111111111"));
    public static readonly BranchId SyntheticBranch2Id = BranchId.From(Guid.Parse("d2222222-2222-2222-2222-222222222222"));
    public static readonly BranchId SyntheticBranch3Id = BranchId.From(Guid.Parse("d3333333-3333-3333-3333-333333333333"));
    public static readonly BranchId SyntheticBranch4Id = BranchId.From(Guid.Parse("d4444444-4444-4444-4444-444444444444"));

    public DevDataSeeder(
        RestaurantOrderDbContext dbContext,
        IHostEnvironment environment,
        ILogger<DevDataSeeder> logger)
    {
        _dbContext = dbContext;
        _environment = environment;
        _logger = logger;
    }

    public async Task<DevSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"FATAL SECURITY VIOLATION: DevDataSeeder cannot be executed in '{_environment.EnvironmentName}' environment. Only 'Development' is permitted.");
        }

        _logger.LogInformation("[DEV SEED] Checking for existing synthetic seed data...");

        var existingTenantCount = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .CountAsync(t => t.Id == SyntheticTenant1Id || t.Id == SyntheticTenant2Id, cancellationToken);

        if (existingTenantCount >= 2)
        {
            _logger.LogInformation("[DEV SEED] Synthetic seed data already present. Skipping creation (idempotent).");
            return new DevSeedResult(0, 0, 0, "Synthetic seed already applied. No changes made.");
        }

        var tenantsCreated = 0;
        var brandsCreated = 0;
        var branchesCreated = 0;

        // Tenant 1: Acme Dining Group
        var tenant1Exists = await _dbContext.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == SyntheticTenant1Id, cancellationToken);
        if (!tenant1Exists)
        {
            var tenant1 = Tenant.Create("Acme Dining Group", "acme-dining", SyntheticTenant1Id);
            _dbContext.Tenants.Add(tenant1);
            tenantsCreated++;

            var brand1 = Brand.Create(SyntheticTenant1Id, "Acme Bistro", "acme-bistro", SyntheticBrand1Id);
            var brand2 = Brand.Create(SyntheticTenant1Id, "Acme Burger & Bar", "acme-burger-bar", SyntheticBrand2Id);
            _dbContext.Brands.AddRange(brand1, brand2);
            brandsCreated += 2;

            var branch1 = Branch.Create(SyntheticTenant1Id, brand1, "Acme Bistro Downtown", "downtown", "Europe/Istanbul", "TRY", SyntheticBranch1Id);
            var branch2 = Branch.Create(SyntheticTenant1Id, brand1, "Acme Bistro Uptown", "uptown", "Europe/Istanbul", "TRY", SyntheticBranch2Id);
            var branch3 = Branch.Create(SyntheticTenant1Id, brand2, "Acme Burger Central", "central", "Europe/Istanbul", "TRY", SyntheticBranch3Id);
            _dbContext.Branches.AddRange(branch1, branch2, branch3);
            branchesCreated += 3;
        }

        // Tenant 2: Solaris Hospitality
        var tenant2Exists = await _dbContext.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == SyntheticTenant2Id, cancellationToken);
        if (!tenant2Exists)
        {
            var tenant2 = Tenant.Create("Solaris Hospitality", "solaris-hospitality", SyntheticTenant2Id);
            _dbContext.Tenants.Add(tenant2);
            tenantsCreated++;

            var brand3 = Brand.Create(SyntheticTenant2Id, "Solaris Rooftop", "solaris-rooftop", SyntheticBrand3Id);
            _dbContext.Brands.Add(brand3);
            brandsCreated++;

            var branch4 = Branch.Create(SyntheticTenant2Id, brand3, "Solaris Sky Lounge", "sky-lounge", "America/New_York", "USD", SyntheticBranch4Id);
            _dbContext.Branches.Add(branch4);
            branchesCreated++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[DEV SEED] Successfully seeded synthetic data: {Tenants} Tenants, {Brands} Brands, {Branches} Branches.",
            tenantsCreated, brandsCreated, branchesCreated);

        return new DevSeedResult(
            tenantsCreated,
            brandsCreated,
            branchesCreated,
            $"Successfully seeded {tenantsCreated} tenants, {brandsCreated} brands, {branchesCreated} branches.");
    }
}
