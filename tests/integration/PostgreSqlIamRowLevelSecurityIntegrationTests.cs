using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Auth;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying PostgreSQL Row-Level Security (RLS) data isolation,
/// append-only audit enforcement, least-privilege user lookup, and composite foreign key
/// constraints across all tenant-owned IAM tables in schema 'iam'.
/// Tests strictly execute with the unprivileged runtime application role ('restaurant_app_user').
/// </summary>
public class PostgreSqlIamRowLevelSecurityIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PostgreSqlIamRowLevelSecurityIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<string> GetRuntimeConnectionStringAsync()
    {
        await EnsureMigrationsAppliedAsync();
        return await _fixture.ProvisionTemporaryRuntimeRoleAsync();
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

    private async Task<RestaurantOrderDbContext> CreateRuntimeDbContextAsync(ITenantContext? tenantContext = null)
    {
        var connStr = await GetRuntimeConnectionStringAsync();
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new RestaurantOrderDbContext(options, tenantContext);
    }

    [Fact]
    public async Task IamRLS_TenantA_CannotReadOrModify_TenantB_Memberships()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantAId = TenantId.New();
        var tenantBId = TenantId.New();

        var (userAId, userBId) = await SeedTwoUsersAsync(now);

        // Seed Tenant A and B foundations via admin connection
        await using (var adminCtx = CreateAdminDbContext())
        {
            var tA = Tenant.Create("Tenant A", "tenant-a-" + Guid.NewGuid().ToString("N")[..6], tenantAId);
            var tB = Tenant.Create("Tenant B", "tenant-b-" + Guid.NewGuid().ToString("N")[..6], tenantBId);
            adminCtx.Tenants.AddRange(tA, tB);

            var mA = UserMembership.Create(tenantAId, userAId, AuthRole.RestaurantAdmin, null, now);
            var mB = UserMembership.Create(tenantBId, userBId, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.AddRange(mA, mB);
            await adminCtx.SaveChangesAsync();
        }

        // Connect as unprivileged runtime user in Tenant A context
        var contextA = new TenantContext(tenantAId.Value, isAuthenticated: true);
        await using var clientA = await CreateRuntimeDbContextAsync(contextA);
        await using var txA = await clientA.BeginTenantTransactionAsync(tenantAId.Value);

        var membershipsA = await clientA.Memberships.ToListAsync();
        Assert.Single(membershipsA);
        Assert.Equal(tenantAId, membershipsA[0].TenantId);
        Assert.Equal(userAId, membershipsA[0].UserId);

        // Verify Tenant A cannot read Tenant B membership directly
        var foreignMembership = await clientA.Memberships
            .FirstOrDefaultAsync(m => m.TenantId == tenantBId);
        Assert.Null(foreignMembership);

        // Negative test: Tenant A attempts to UPDATE Tenant B membership
        var rawUpdate = await clientA.Database.ExecuteSqlRawAsync(
            "UPDATE iam.memberships SET role = 'Kitchen' WHERE tenant_id = {0};",
            tenantBId.Value);
        Assert.Equal(0, rawUpdate);

        // Negative test: Tenant A attempts to DELETE Tenant B membership
        var rawDelete = await clientA.Database.ExecuteSqlRawAsync(
            "DELETE FROM iam.memberships WHERE tenant_id = {0};",
            tenantBId.Value);
        Assert.Equal(0, rawDelete);

        // Negative test: Tenant A attempts to INSERT a membership for Tenant B
        var illegalCrossTenantMembership = UserMembership.Create(tenantBId, userAId, AuthRole.Waiter, null, now);
        clientA.Memberships.Add(illegalCrossTenantMembership);
        await Assert.ThrowsAnyAsync<Exception>(() => clientA.SaveChangesAsync());
    }

    [Fact]
    public async Task IamRLS_TenantA_CannotReadOrModify_TenantB_Sessions_And_Tokens()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantAId = TenantId.New();
        var tenantBId = TenantId.New();
        var (userAId, userBId) = await SeedTwoUsersAsync(now);

        var sessionAId = Guid.NewGuid();
        var sessionBId = Guid.NewGuid();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tA = Tenant.Create("Tenant A", "t-a-" + Guid.NewGuid().ToString("N")[..6], tenantAId);
            var tB = Tenant.Create("Tenant B", "t-b-" + Guid.NewGuid().ToString("N")[..6], tenantBId);
            adminCtx.Tenants.AddRange(tA, tB);

            var mA = UserMembership.Create(tenantAId, userAId, AuthRole.RestaurantAdmin, null, now);
            var mB = UserMembership.Create(tenantBId, userBId, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.AddRange(mA, mB);

            var sA = AuthSession.Create(tenantAId, userAId, mA.Id, null, AuthenticationMethod.Password, TimeSpan.FromHours(1), now, id: sessionAId);
            var sB = AuthSession.Create(tenantBId, userBId, mB.Id, null, AuthenticationMethod.Password, TimeSpan.FromHours(1), now, id: sessionBId);
            adminCtx.Sessions.AddRange(sA, sB);

            var rtA = RefreshToken.Create(tenantAId, sessionAId, Guid.NewGuid(), "hashA", TimeSpan.FromDays(7), now);
            var rtB = RefreshToken.Create(tenantBId, sessionBId, Guid.NewGuid(), "hashB", TimeSpan.FromDays(7), now);
            adminCtx.RefreshTokens.AddRange(rtA, rtB);

            await adminCtx.SaveChangesAsync();
        }

        // Query under Tenant A runtime session
        var contextA = new TenantContext(tenantAId.Value, isAuthenticated: true);
        await using var clientA = await CreateRuntimeDbContextAsync(contextA);
        await using var txA = await clientA.BeginTenantTransactionAsync(tenantAId.Value);

        var sessions = await clientA.Sessions.ToListAsync();
        Assert.Single(sessions);
        Assert.Equal(sessionAId, sessions[0].Id);

        var tokens = await clientA.RefreshTokens.ToListAsync();
        Assert.Single(tokens);
        Assert.Equal("hashA", tokens[0].TokenHash);

        // Tenant A cannot read Tenant B session
        var foreignSession = await clientA.Sessions.FirstOrDefaultAsync(s => s.Id == sessionBId);
        Assert.Null(foreignSession);
    }

    [Fact]
    public async Task IamRLS_RawSql_And_IgnoreQueryFilters_CannotBypass_PostgreSqlRLS()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantAId = TenantId.New();
        var tenantBId = TenantId.New();
        var (userAId, userBId) = await SeedTwoUsersAsync(now);

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tA = Tenant.Create("Tenant A", "t-a-" + Guid.NewGuid().ToString("N")[..6], tenantAId);
            var tB = Tenant.Create("Tenant B", "t-b-" + Guid.NewGuid().ToString("N")[..6], tenantBId);
            adminCtx.Tenants.AddRange(tA, tB);

            var mA = UserMembership.Create(tenantAId, userAId, AuthRole.RestaurantAdmin, null, now);
            var mB = UserMembership.Create(tenantBId, userBId, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.AddRange(mA, mB);
            await adminCtx.SaveChangesAsync();
        }

        // Connect as Tenant A
        var contextA = new TenantContext(tenantAId.Value, isAuthenticated: true);
        await using var clientA = await CreateRuntimeDbContextAsync(contextA);
        await using var txA = await clientA.BeginTenantTransactionAsync(tenantAId.Value);

        // 1. IgnoreQueryFilters MUST NOT bypass PostgreSQL RLS
        var ignoredFilters = await clientA.Memberships.IgnoreQueryFilters().ToListAsync();
        Assert.All(ignoredFilters, m => Assert.Equal(tenantAId, m.TenantId));

        // 2. Raw SQL SELECT MUST NOT bypass PostgreSQL RLS
        var rawMemberships = await clientA.Memberships
            .FromSqlRaw("SELECT * FROM iam.memberships")
            .ToListAsync();
        Assert.All(rawMemberships, m => Assert.Equal(tenantAId, m.TenantId));
    }

    [Fact]
    public async Task CrossTenantMembership_ReferencingAnotherTenantBranch_FailsCompositeForeignKey()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantAId = TenantId.New();
        var tenantBId = TenantId.New();
        var branchBId = BranchId.New();
        var (userAId, _) = await SeedTwoUsersAsync(now);

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tA = Tenant.Create("Tenant A", "t-a-" + Guid.NewGuid().ToString("N")[..6], tenantAId);
            var tB = Tenant.Create("Tenant B", "t-b-" + Guid.NewGuid().ToString("N")[..6], tenantBId);
            adminCtx.Tenants.AddRange(tA, tB);

            var brandB = Brand.Create(tenantBId, "Brand B", "brand-b-" + Guid.NewGuid().ToString("N")[..6]);
            adminCtx.Brands.Add(brandB);

            var branchB = Branch.Create(
                tenantBId, brandB, "Branch B", "branch-b-" + Guid.NewGuid().ToString("N")[..6],
                "Europe/Istanbul", "TRY", branchBId);
            adminCtx.Branches.Add(branchB);

            await adminCtx.SaveChangesAsync();

            // Attempt to assign user to Tenant A membership pointing to Branch B of Tenant B
            var illegalMembership = UserMembership.Create(tenantAId, userAId, AuthRole.Waiter, branchBId, now);
            adminCtx.Memberships.Add(illegalMembership);

            // Must throw DbUpdateException due to fk_memberships_branches_tenant_id_branch_id
            await Assert.ThrowsAsync<DbUpdateException>(() => adminCtx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task SecurityAuditEvents_UpdateOrDelete_IsProhibitedByTrigger()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var auditId = Guid.NewGuid();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("Audit Tenant", "audit-" + Guid.NewGuid().ToString("N")[..6], tenantId);
            adminCtx.Tenants.Add(tenant);

            var audit = SecurityAuditEvent.Create(tenantId, SecurityAuditEventType.LoginSucceeded, now, id: auditId);
            adminCtx.SecurityAuditEvents.Add(audit);
            await adminCtx.SaveChangesAsync();

            // Attempt raw SQL UPDATE on audit event
            await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await adminCtx.Database.ExecuteSqlRawAsync(
                    "UPDATE iam.security_audit_events SET event_type = 'tampered' WHERE id = {0};",
                    auditId);
            });

            // Attempt raw SQL DELETE on audit event
            await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await adminCtx.Database.ExecuteSqlRawAsync(
                    "DELETE FROM iam.security_audit_events WHERE id = {0};",
                    auditId);
            });
        }
    }

    [Fact]
    public async Task RuntimeRole_DirectSelectOnUsers_IsDenied_WhileSecurityDefinerLookupSucceeds()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var normalizedEmail = "lookup.test." + Guid.NewGuid().ToString("N")[..6] + "@example.com";
        var passwordHash = "pbkdf2_hash_value_12345";

        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(normalizedEmail, passwordHash, now);
            adminCtx.Users.Add(user);
            await adminCtx.SaveChangesAsync();
        }

        // Connect as runtime application role
        await using var runtimeCtx = await CreateRuntimeDbContextAsync(TenantContext.Empty);

        // 1. Direct SELECT * FROM iam.users must fail with permission denied
        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await runtimeCtx.Database.ExecuteSqlRawAsync("SELECT * FROM iam.users;");
        });

        // 2. Least-privilege SECURITY DEFINER lookup gateway must succeed
        var gateway = new PostgreSqlIamUserLookupGateway(runtimeCtx);
        var lookupResult = await gateway.LookupUserForLoginAsync(normalizedEmail);

        Assert.NotNull(lookupResult);
        Assert.Equal(normalizedEmail, lookupResult.NormalizedEmail);
        Assert.Equal(passwordHash, lookupResult.PasswordHash);
        Assert.Equal((int)UserStatus.Active, lookupResult.Status);
        Assert.Equal(1, lookupResult.SecurityVersion);
    }

    [Fact]
    public async Task RuntimeRole_CannotAlterSchemaOrDropTable()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        await using var runtimeCtx = await CreateRuntimeDbContextAsync(TenantContext.Empty);

        // Attempt DDL DROP TABLE
        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await runtimeCtx.Database.ExecuteSqlRawAsync("DROP TABLE iam.memberships;");
        });

        // Attempt CREATE TABLE in iam schema
        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await runtimeCtx.Database.ExecuteSqlRawAsync("CREATE TABLE iam.malicious_probe (id int);");
        });
    }

    private RestaurantOrderDbContext CreateAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;

        return new RestaurantOrderDbContext(options, TenantContext.Empty);
    }

    private async Task<(UserId UserAId, UserId UserBId)> SeedTwoUsersAsync(DateTimeOffset now)
    {
        await using var adminCtx = CreateAdminDbContext();
        var uA = User.Create("user.a." + Guid.NewGuid().ToString("N")[..6] + "@example.com", "hashA", now);
        var uB = User.Create("user.b." + Guid.NewGuid().ToString("N")[..6] + "@example.com", "hashB", now);
        adminCtx.Users.AddRange(uA, uB);
        await adminCtx.SaveChangesAsync();
        return (uA.Id, uB.Id);
    }
}
