using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Auth;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// End-to-end integration tests under the unprivileged runtime role ('restaurant_app_user')
/// verifying fail-closed tenant transactions, connection pool context isolation,
/// password login, invitation acceptance, password reset, terminal enrollment, and PIN auth.
/// </summary>
public class PostgreSqlIamEndToEndAuthIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PostgreSqlIamEndToEndAuthIntegrationTests(TestcontainersFixture fixture)
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
    public async Task SetTenantSessionAsync_WithoutActiveTransaction_ThrowsInvalidOperationException()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var tenantId = Guid.NewGuid();
        await using var runtimeCtx = await CreateRuntimeDbContextAsync();

        // Must fail-closed if no transaction is active
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await runtimeCtx.SetTenantSessionAsync(tenantId);
        });

        Assert.Contains("active database transaction", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectionPool_TenantContext_DoesNotLeakAcrossTransactions()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantAId = TenantId.New();
        var (userAId, _) = await SeedUsersAsync(now);

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tA = Tenant.Create("Pool Tenant", "pool-" + Guid.NewGuid().ToString("N")[..6], tenantAId);
            adminCtx.Tenants.Add(tA);
            var mA = UserMembership.Create(tenantAId, userAId, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.Add(mA);
            await adminCtx.SaveChangesAsync();
        }

        var connStr = await GetRuntimeConnectionStringAsync();

        // Use same underlying physical connection across two distinct operations
        await using var connection = new NpgsqlConnection(connStr);
        await connection.OpenAsync();

        // Step 1: Set tenant context inside transaction and verify visibility
        await using (var tx1 = await connection.BeginTransactionAsync())
        {
            await using var cmd1 = connection.CreateCommand();
            cmd1.Transaction = tx1;
            cmd1.CommandText = "SELECT set_config('app.current_tenant_id', @tId, true);";
            cmd1.Parameters.AddWithValue("tId", tenantAId.Value.ToString());
            await cmd1.ExecuteNonQueryAsync();

            await using var countCmd1 = connection.CreateCommand();
            countCmd1.Transaction = tx1;
            countCmd1.CommandText = "SELECT COUNT(*) FROM iam.memberships;";
            var count1 = (long)(await countCmd1.ExecuteScalarAsync())!;
            Assert.True(count1 >= 1);

            await tx1.CommitAsync();
        }

        // Step 2: In a subsequent statement on the same connection WITHOUT setting tenant,
        // app.current_tenant_id MUST NOT leak; query must return 0 rows
        await using (var tx2 = await connection.BeginTransactionAsync())
        {
            await using var countCmd2 = connection.CreateCommand();
            countCmd2.Transaction = tx2;
            countCmd2.CommandText = "SELECT COUNT(*) FROM iam.memberships;";
            var count2 = (long)(await countCmd2.ExecuteScalarAsync())!;
            Assert.Equal(0L, count2);

            await tx2.RollbackAsync();
        }
    }

    [Fact]
    public async Task PasswordLoginFlow_WorksEndToEnd_UnderRuntimeRole()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var tenantSlug = $"login-{Guid.NewGuid():N}"[..12];
        var email = $"login.{Guid.NewGuid():N}@example.com";
        var password = "SecurePassword123!";
        var passwordHasher = new AspNetCorePasswordHasher();
        var passwordHash = passwordHasher.HashPassword(password).Hash;

        UserId userId;
        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(email, passwordHash, now);
            adminCtx.Users.Add(user);

            var tenant = Tenant.Create("Login Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);

            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.Add(membership);

            await adminCtx.SaveChangesAsync();
            userId = user.Id;
        }

        // Execute login using runtime context
        await using var runtimeCtx = await CreateRuntimeDbContextAsync();
        var bootstrapGateway = new PostgreSqlIamBootstrapGateway(runtimeCtx);
        var userLookupGateway = new PostgreSqlIamUserLookupGateway(runtimeCtx);
        var jwtSettings = Options.Create(new JwtSettings
        {
            Secret = "a-very-secret-test-jwt-key-with-at-least-32-chars!",
            Issuer = "RestaurantOrder",
            Audience = "RestaurantOrderApp",
            AccessTokenLifetimeMinutes = 15
        });
        var jwtGen = new JwtTokenService(jwtSettings);
        var refreshSvc = new RefreshTokenService();
        var platformStore = new PostgreSqlPlatformSessionStore(runtimeCtx);
        var rateLimiter = new LoginRateLimiter();
        var sessionMgr = new AuthSessionManager(platformStore, bootstrapGateway);
        var authSettings = Options.Create(new AuthSettings());

        var authService = new AuthService(
            runtimeCtx,
            userLookupGateway,
            bootstrapGateway,
            passwordHasher,
            jwtGen,
            refreshSvc,
            platformStore,
            rateLimiter,
            sessionMgr,
            authSettings,
            NullLogger<AuthService>.Instance);

        var loginResult = await authService.LoginAsync(new LoginCommand(
            Email: email,
            Password: password,
            TenantSlug: tenantSlug));

        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.AccessToken);
        Assert.NotNull(loginResult.RefreshToken);
        Assert.Equal(userId.Value, loginResult.User.UserId);
    }

    [Fact]
    public async Task InvitationAccept_And_PasswordReset_WorkEndToEnd_UnderRuntimeRole()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var email = $"invitee.{Guid.NewGuid():N}@example.com";
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("Invite Tenant", $"invite-{Guid.NewGuid():N}"[..12], tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = RestaurantOrder.Domain.Brands.Brand.Create(tenantId, "Invite Brand", $"ib-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var branch = Branch.Create(tenantId, brand, "Invite Branch", $"ibr-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);

            await adminCtx.SaveChangesAsync();
        }

        // 1. Invite Staff via StaffIdentityService
        await using var runtimeCtx = await CreateRuntimeDbContextAsync();
        var bootstrapGateway = new PostgreSqlIamBootstrapGateway(runtimeCtx);
        var userLookupGateway = new PostgreSqlIamUserLookupGateway(runtimeCtx);

        var notificationSender = new TestSinkIdentityNotificationSender(NullLogger<TestSinkIdentityNotificationSender>.Instance);
        var platformSessionStore = new PostgreSqlPlatformSessionStore(runtimeCtx);
        var sessionManager = new AuthSessionManager(platformSessionStore, bootstrapGateway);

        var identityService = new StaffIdentityService(
            runtimeCtx,
            bootstrapGateway,
            userLookupGateway,
            passwordHasher,
            notificationSender,
            sessionManager);

        var adminPrincipal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        var inviteResult = await identityService.InviteStaffAsync(
            tenantId,
            new InviteStaffCommand(email, AuthRole.Waiter, branchId.Value),
            adminPrincipal);

        Assert.NotNull(notificationSender.LastInvitationToken);
        var invitationToken = notificationSender.LastInvitationToken!;

        // 2. Accept Invitation
        var newPassword = "StaffNewPassword123!";
        await identityService.AcceptInvitationAsync(new AcceptInvitationCommand(
            invitationToken,
            newPassword));

        // 3. Request Password Reset
        var resetToken = await identityService.RequestPasswordResetAsync(new RequestPasswordResetCommand(email));
        Assert.NotNull(resetToken);
        Assert.NotNull(notificationSender.LastResetToken);

        // 4. Complete Password Reset
        var finalPassword = "StaffResetPassword456!";
        await identityService.ResetPasswordAsync(new ResetPasswordCommand(notificationSender.LastResetToken!, finalPassword));
    }

    [Fact]
    public async Task TerminalEnrollment_And_PinLogin_WorkEndToEnd_UnderRuntimeRole()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var email = $"waiter.{Guid.NewGuid():N}@example.com";
        var passwordHasher = new AspNetCorePasswordHasher();
        var pinHasherOptions = Options.Create(new PinHasherOptions { PepperValue = "test-pepper" });
        var pinHasher = new PepperedPinHasher(pinHasherOptions);

        UserId userId;
        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("Terminal Tenant", $"term-{Guid.NewGuid():N}"[..12], tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = RestaurantOrder.Domain.Brands.Brand.Create(tenantId, "Term Brand", $"tb-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var branch = Branch.Create(tenantId, brand, "Term Branch", $"tbr-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);

            var user = User.Create(email, passwordHasher.HashPassword("DummyPass123!").Hash, now);
            adminCtx.Users.Add(user);

            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.Waiter, branchId, now);
            adminCtx.Memberships.Add(membership);

            await adminCtx.SaveChangesAsync();
            userId = user.Id;
        }

        // 1. Create Terminal Enrollment Code
        await using var runtimeCtx = await CreateRuntimeDbContextAsync();
        var enrollmentStore = new InMemoryTerminalEnrollmentStore();
        var bootstrapGateway = new PostgreSqlIamBootstrapGateway(runtimeCtx);
        var userLookupGateway = new PostgreSqlIamUserLookupGateway(runtimeCtx);

        var terminalService = new TrustedTerminalService(
            runtimeCtx,
            bootstrapGateway,
            enrollmentStore);

        var enrollResult = await terminalService.CreateEnrollmentCodeAsync(
            tenantId,
            new EnrollTerminalCommand(branchId.Value, "POS-01", "dev-hw-123"),
            userId);

        Assert.NotNull(enrollResult.EnrollmentCode);

        // 2. Activate Terminal
        var activateResult = await terminalService.ActivateTerminalAsync(new ActivateTerminalCommand(
            EnrollmentCode: enrollResult.EnrollmentCode,
            TerminalName: "POS-01",
            DeviceIdentifier: "dev-hw-123"));

        Assert.NotNull(activateResult.DeviceSecret);

        // 3. Set Staff PIN
        var pinLimiter = new TerminalPinRateLimiter();
        var jwtSettings = Options.Create(new JwtSettings
        {
            Secret = "a-very-secret-test-jwt-key-with-at-least-32-chars!",
            Issuer = "RestaurantOrder",
            Audience = "RestaurantOrderApp",
            AccessTokenLifetimeMinutes = 15
        });
        var jwtGen = new JwtTokenService(jwtSettings);
        var refreshSvc = new RefreshTokenService();

        var pinAuthService = new StaffPinAuthService(
            runtimeCtx,
            bootstrapGateway,
            userLookupGateway,
            terminalService,
            pinLimiter,
            pinHasher,
            jwtGen,
            refreshSvc);

        await pinAuthService.SetPinAsync(
            tenantId,
            new SetStaffPinCommand(userId.Value, branchId.Value, "1234"),
            userId);

        // 4. Login With PIN
        var pinLoginResult = await pinAuthService.LoginWithPinAsync(new PinLoginCommand(
            TerminalId: activateResult.TerminalId,
            DeviceSecret: activateResult.DeviceSecret,
            Pin: "1234",
            UserId: userId.Value,
            Email: null,
            IpAddress: "127.0.0.1"));

        Assert.NotNull(pinLoginResult);
        Assert.NotNull(pinLoginResult.AccessToken);
        Assert.Equal(userId.Value, pinLoginResult.User.UserId);
    }

    private async Task<(UserId UserAId, UserId UserBId)> SeedUsersAsync(DateTimeOffset now)
    {
        await using var adminCtx = CreateAdminDbContext();
        var uA = User.Create($"ua.{Guid.NewGuid():N}@example.com", "hashA", now);
        var uB = User.Create($"ub.{Guid.NewGuid():N}@example.com", "hashB", now);
        adminCtx.Users.AddRange(uA, uB);
        await adminCtx.SaveChangesAsync();
        return (uA.Id, uB.Id);
    }
}
