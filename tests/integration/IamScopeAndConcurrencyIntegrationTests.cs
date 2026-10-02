using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
/// Verifies atomic single-statement token consumption under parallel execution races,
/// multi-tenant membership status isolation, and branch manager scope enforcement.
/// </summary>
public class IamScopeAndConcurrencyIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public IamScopeAndConcurrencyIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<string> GetRuntimeConnectionStringAsync()
    {
        return await _fixture.ProvisionTemporaryRuntimeRoleAsync();
    }

    private async Task<RestaurantOrderDbContext> CreateRuntimeDbContextAsync(ITenantContext? tenantContext = null)
    {
        var connStr = await GetRuntimeConnectionStringAsync();
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new RestaurantOrderDbContext(options, tenantContext);
    }

    private RestaurantOrderDbContext CreateAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;

        return new RestaurantOrderDbContext(options, TenantContext.Empty);
    }

    [Fact]
    public async Task AcceptInvitation_ParallelRequests_AllowsExactlyOneToSucceed()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var email = $"parallel.invite.{Guid.NewGuid():N}@example.com";
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("Parallel Tenant", $"pt-{Guid.NewGuid():N}"[..12], tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = Brand.Create(tenantId, "Parallel Brand", $"pb-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var branch = Branch.Create(tenantId, brand, "Parallel Branch", $"pbr-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);

            await adminCtx.SaveChangesAsync();
        }

        await using var setupCtx = await CreateRuntimeDbContextAsync();
        var setupBootstrap = new PostgreSqlIamBootstrapGateway(setupCtx);
        var setupLookup = new PostgreSqlIamUserLookupGateway(setupCtx);
        var notificationSender = new TestSinkIdentityNotificationSender(NullLogger<TestSinkIdentityNotificationSender>.Instance);
        var sessionStore = new PostgreSqlPlatformSessionStore(setupCtx);
        var sessionManager = new AuthSessionManager(sessionStore, setupBootstrap);

        var setupIdentity = new StaffIdentityService(
            setupCtx, setupBootstrap, setupLookup, passwordHasher, notificationSender, sessionManager);

        var adminPrincipal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        await setupIdentity.InviteStaffAsync(
            tenantId,
            new InviteStaffCommand(email, AuthRole.Waiter, branchId.Value),
            adminPrincipal);

        var rawToken = notificationSender.LastInvitationToken;
        Assert.NotNull(rawToken);

        // Spawn two distinct runtime contexts to simulate two separate API instances
        await using var runtimeCtx1 = await CreateRuntimeDbContextAsync();
        var bootstrap1 = new PostgreSqlIamBootstrapGateway(runtimeCtx1);
        var lookup1 = new PostgreSqlIamUserLookupGateway(runtimeCtx1);
        var identityService1 = new StaffIdentityService(
            runtimeCtx1, bootstrap1, lookup1, passwordHasher, notificationSender, sessionManager);

        await using var runtimeCtx2 = await CreateRuntimeDbContextAsync();
        var bootstrap2 = new PostgreSqlIamBootstrapGateway(runtimeCtx2);
        var lookup2 = new PostgreSqlIamUserLookupGateway(runtimeCtx2);
        var identityService2 = new StaffIdentityService(
            runtimeCtx2, bootstrap2, lookup2, passwordHasher, notificationSender, sessionManager);

        var command = new AcceptInvitationCommand(rawToken, "SecretPass123!");

        // Execute parallel consumption race
        var t1 = Task.Run(async () =>
        {
            try
            {
                await identityService1.AcceptInvitationAsync(command);
                return null as Exception;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                await identityService2.AcceptInvitationAsync(command);
                return null as Exception;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var results = await Task.WhenAll(t1, t2);

        // Exactly one request must succeed; exactly one must fail with InvalidOperationException
        Assert.Single(results, r => r == null);
        Assert.Single(results, r => r is InvalidOperationException);
    }

    [Fact]
    public async Task ResetPassword_ParallelRequests_AllowsExactlyOneToSucceed()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var email = $"parallel.reset.{Guid.NewGuid():N}@example.com";
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("Reset Tenant", $"rt-{Guid.NewGuid():N}"[..12], tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = Brand.Create(tenantId, "Reset Brand", $"rb-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var branch = Branch.Create(tenantId, brand, "Reset Branch", $"rbr-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);

            await adminCtx.SaveChangesAsync();
        }

        await using var setupCtx = await CreateRuntimeDbContextAsync();
        var setupBootstrap = new PostgreSqlIamBootstrapGateway(setupCtx);
        var setupLookup = new PostgreSqlIamUserLookupGateway(setupCtx);
        var notificationSender = new TestSinkIdentityNotificationSender(NullLogger<TestSinkIdentityNotificationSender>.Instance);
        var sessionStore = new PostgreSqlPlatformSessionStore(setupCtx);
        var sessionManager = new AuthSessionManager(sessionStore, setupBootstrap);

        var setupIdentity = new StaffIdentityService(
            setupCtx, setupBootstrap, setupLookup, passwordHasher, notificationSender, sessionManager);

        var adminPrincipal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        await setupIdentity.InviteStaffAsync(
            tenantId,
            new InviteStaffCommand(email, AuthRole.Waiter, branchId.Value),
            adminPrincipal);

        var inviteToken = notificationSender.LastInvitationToken;
        Assert.NotNull(inviteToken);

        await setupIdentity.AcceptInvitationAsync(new AcceptInvitationCommand(inviteToken, "InitialPassword123!"));

        // Request password reset
        await setupIdentity.RequestPasswordResetAsync(new RequestPasswordResetCommand(email));
        var resetToken = notificationSender.LastResetToken;
        Assert.NotNull(resetToken);

        // Spawn two distinct runtime contexts
        await using var runtimeCtx1 = await CreateRuntimeDbContextAsync();
        var bootstrap1 = new PostgreSqlIamBootstrapGateway(runtimeCtx1);
        var lookup1 = new PostgreSqlIamUserLookupGateway(runtimeCtx1);
        var identityService1 = new StaffIdentityService(
            runtimeCtx1, bootstrap1, lookup1, passwordHasher, notificationSender, sessionManager);

        await using var runtimeCtx2 = await CreateRuntimeDbContextAsync();
        var bootstrap2 = new PostgreSqlIamBootstrapGateway(runtimeCtx2);
        var lookup2 = new PostgreSqlIamUserLookupGateway(runtimeCtx2);
        var identityService2 = new StaffIdentityService(
            runtimeCtx2, bootstrap2, lookup2, passwordHasher, notificationSender, sessionManager);

        var command = new ResetPasswordCommand(resetToken, "NewSecurePassword456!");

        var t1 = Task.Run(async () =>
        {
            try
            {
                await identityService1.ResetPasswordAsync(command);
                return null as Exception;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                await identityService2.ResetPasswordAsync(command);
                return null as Exception;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var results = await Task.WhenAll(t1, t2);

        // Exactly one request must succeed; exactly one must fail with InvalidOperationException
        Assert.Single(results, r => r == null);
        Assert.Single(results, r => r is InvalidOperationException);
    }

    [Fact]
    public async Task MultiTenant_SuspensionIsolation_TenantASuspendDoesNotAffectTenantB()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantAId = TenantId.New();
        var tenantBId = TenantId.New();
        var branchAId = BranchId.New();
        var branchBId = BranchId.New();
        var slugA = $"ta-{Guid.NewGuid():N}"[..12];
        var slugB = $"tb-{Guid.NewGuid():N}"[..12];
        var email = $"multi.{Guid.NewGuid():N}@example.com";
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenantA = Tenant.Create("Tenant A", slugA, tenantAId);
            var tenantB = Tenant.Create("Tenant B", slugB, tenantBId);
            adminCtx.Tenants.AddRange(tenantA, tenantB);

            var brandA = Brand.Create(tenantAId, "Brand A", $"ba-{Guid.NewGuid():N}"[..10]);
            var brandB = Brand.Create(tenantBId, "Brand B", $"bb-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.AddRange(brandA, brandB);

            var branchA = Branch.Create(tenantAId, brandA, "Branch A", $"bra-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchAId);
            var branchB = Branch.Create(tenantBId, brandB, "Branch B", $"brb-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchBId);
            adminCtx.Branches.AddRange(branchA, branchB);

            await adminCtx.SaveChangesAsync();
        }

        await using var runtimeCtx = await CreateRuntimeDbContextAsync();
        var bootstrap = new PostgreSqlIamBootstrapGateway(runtimeCtx);
        var lookup = new PostgreSqlIamUserLookupGateway(runtimeCtx);
        var notificationSender = new TestSinkIdentityNotificationSender(NullLogger<TestSinkIdentityNotificationSender>.Instance);
        var sessionStore = new PostgreSqlPlatformSessionStore(runtimeCtx);
        var sessionManager = new AuthSessionManager(sessionStore, bootstrap);

        var identityService = new StaffIdentityService(
            runtimeCtx, bootstrap, lookup, passwordHasher, notificationSender, sessionManager);

        var adminPrincipalA = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(), role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantAId), sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password, securityVersion: 1);

        var adminPrincipalB = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(), role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantBId), sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password, securityVersion: 1);

        // Invite to Tenant A
        var inviteA = await identityService.InviteStaffAsync(
            tenantAId, new InviteStaffCommand(email, AuthRole.Waiter, branchAId.Value), adminPrincipalA);
        var tokenA = notificationSender.LastInvitationToken!;

        await identityService.AcceptInvitationAsync(new AcceptInvitationCommand(tokenA, "SharedPassword123!"));

        // Invite same user to Tenant B
        var inviteB = await identityService.InviteStaffAsync(
            tenantBId, new InviteStaffCommand(email, AuthRole.Cashier, branchBId.Value), adminPrincipalB);
        var tokenB = notificationSender.LastInvitationToken!;

        await identityService.AcceptInvitationAsync(new AcceptInvitationCommand(tokenB, "SharedPassword123!"));

        // Now Admin A suspends user's membership in Tenant A
        await identityService.UpdateStaffStatusAsync(
            tenantAId,
            new UpdateStaffStatusCommand(inviteA.UserId, UserMembershipStatus.Suspended, branchAId.Value),
            adminPrincipalA);

        // Verify that Tenant A membership is Suspended while Tenant B membership is Active
        await using (var adminCtx = CreateAdminDbContext())
        {
            var memA = await adminCtx.Memberships.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == tenantAId && m.UserId == UserId.From(inviteA.UserId));
            var memB = await adminCtx.Memberships.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == tenantBId && m.UserId == UserId.From(inviteA.UserId));
            var user = await adminCtx.Users.FirstOrDefaultAsync(u => u.Id == UserId.From(inviteA.UserId));

            Assert.NotNull(memA);
            Assert.Equal(UserMembershipStatus.Suspended, memA.Status);
            Assert.False(memA.IsActive);

            Assert.NotNull(memB);
            Assert.Equal(UserMembershipStatus.Active, memB.Status);
            Assert.True(memB.IsActive);

            Assert.NotNull(user);
            Assert.Equal(UserStatus.Active, user.Status); // Global user status remains Active!
        }

        // Pre-auth lookup for Tenant A fails (returns null)
        var preAuthA = await bootstrap.LookupMembershipForLoginAsync(tenantAId.Value, inviteA.UserId);
        Assert.Null(preAuthA);

        // Pre-auth lookup for Tenant B succeeds
        var preAuthB = await bootstrap.LookupMembershipForLoginAsync(tenantBId.Value, inviteA.UserId);
        Assert.NotNull(preAuthB);
        Assert.True(preAuthB.IsActive);
    }

    [Fact]
    public async Task BranchManager_CannotManageStaffOutsideBranch()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var tenantId = TenantId.New();
        var branch1Id = BranchId.New();
        var branch2Id = BranchId.New();
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("BM Scope Tenant", $"bmt-{Guid.NewGuid():N}"[..12], tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = Brand.Create(tenantId, "BM Brand", $"bmb-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var b1 = Branch.Create(tenantId, brand, "Branch 1", $"bm1-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branch1Id);
            var b2 = Branch.Create(tenantId, brand, "Branch 2", $"bm2-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branch2Id);
            adminCtx.Branches.AddRange(b1, b2);

            await adminCtx.SaveChangesAsync();
        }

        await using var runtimeCtx = await CreateRuntimeDbContextAsync();
        var bootstrap = new PostgreSqlIamBootstrapGateway(runtimeCtx);
        var lookup = new PostgreSqlIamUserLookupGateway(runtimeCtx);
        var notificationSender = new TestSinkIdentityNotificationSender(NullLogger<TestSinkIdentityNotificationSender>.Instance);
        var sessionStore = new PostgreSqlPlatformSessionStore(runtimeCtx);
        var sessionManager = new AuthSessionManager(sessionStore, bootstrap);

        var identityService = new StaffIdentityService(
            runtimeCtx, bootstrap, lookup, passwordHasher, notificationSender, sessionManager);

        var branchManagerPrincipal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.BranchManager,
            scope: AuthorizationScope.ForBranch(tenantId, branch1Id),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        // 1. Cannot invite staff to Branch 2
        await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(async () =>
        {
            await identityService.InviteStaffAsync(
                tenantId,
                new InviteStaffCommand("staff2@example.com", AuthRole.Waiter, branch2Id.Value),
                branchManagerPrincipal);
        });

        // 2. Cannot list staff from Branch 2
        await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(async () =>
        {
            await identityService.ListStaffAsync(
                tenantId,
                branch2Id.Value,
                branchManagerPrincipal);
        });

        // 3. Cannot invite administrative role
        await Assert.ThrowsAsync<InvalidAuthorizationScopeException>(async () =>
        {
            await identityService.InviteStaffAsync(
                tenantId,
                new InviteStaffCommand("admin@example.com", AuthRole.RestaurantAdmin, branch1Id.Value),
                branchManagerPrincipal);
        });
    }
}
