using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
using RestaurantOrder.Infrastructure.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying distributed multi-instance authentication scenarios across Instance A and B.
/// Validates PostgreSQL persistence for sessions and refresh tokens, atomic rotation with row-level locks,
/// token family revocation on reuse, Redis-backed rate limiting and lockout, single-use enrollment (GETDEL),
/// immediate token revocation, and restart safety.
/// </summary>
public class PostgreSqlDistributedAuthIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PostgreSqlDistributedAuthIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private RestaurantOrderDbContext CreateAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        return new RestaurantOrderDbContext(options, TenantContext.Empty);
    }

    private async Task<TestApiInstance> CreateInstanceAsync(string instanceName)
    {
        var connStr = await _fixture.ProvisionTemporaryRuntimeRoleAsync();
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(connStr)
            .Options;

        var dbContext = new RestaurantOrderDbContext(options, TenantContext.Empty);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REDIS_URL"] = _fixture.RedisEndpoint,
                ["Jwt:Secret"] = "a-very-secret-distributed-jwt-test-key-32-chars!",
                ["Jwt:Issuer"] = "RestaurantOrder",
                ["Jwt:Audience"] = "RestaurantOrderApp",
                ["Jwt:AccessTokenLifetimeMinutes"] = "15"
            })
            .Build();

        var env = new DistributedTestHostEnvironment { EnvironmentName = "Testing" };
        var redisProvider = new StackExchangeRedisDatabaseProvider(
            config,
            env,
            NullLogger<StackExchangeRedisDatabaseProvider>.Instance);

        var bootstrapGateway = new PostgreSqlIamBootstrapGateway(dbContext);
        var userLookupGateway = new PostgreSqlIamUserLookupGateway(dbContext);
        var platformSessionStore = new PostgreSqlPlatformSessionStore(dbContext);
        var passwordHasher = new AspNetCorePasswordHasher();
        var jwtSettings = Options.Create(new JwtSettings
        {
            Secret = "a-very-secret-distributed-jwt-test-key-32-chars!",
            Issuer = "RestaurantOrder",
            Audience = "RestaurantOrderApp",
            AccessTokenLifetimeMinutes = 15
        });
        var jwtGen = new JwtTokenService(jwtSettings);
        var refreshSvc = new RefreshTokenService();
        var authSettings = Options.Create(new AuthSettings());

        var loginRateLimiter = new RedisLoginRateLimiter(redisProvider, env);
        var terminalEnrollmentStore = new RedisTerminalEnrollmentStore(redisProvider, env);
        var terminalPinRateLimiter = new RedisTerminalPinRateLimiter(redisProvider, env);
        var tokenValidator = new DistributedTokenRevocationValidator(
            dbContext,
            redisProvider,
            env,
            NullLogger<DistributedTokenRevocationValidator>.Instance);

        var sessionManager = new AuthSessionManager(platformSessionStore, bootstrapGateway, tokenValidator);
        var authService = new AuthService(
            dbContext,
            userLookupGateway,
            bootstrapGateway,
            passwordHasher,
            jwtGen,
            refreshSvc,
            platformSessionStore,
            loginRateLimiter,
            sessionManager,
            authSettings,
            NullLogger<AuthService>.Instance);

        var terminalService = new TrustedTerminalService(
            dbContext,
            bootstrapGateway,
            terminalEnrollmentStore);

        var pinHasher = new PepperedPinHasher(Options.Create(new PinHasherOptions { PepperValue = "test-pepper" }));
        var pinAuthService = new StaffPinAuthService(
            dbContext,
            bootstrapGateway,
            userLookupGateway,
            terminalService,
            terminalPinRateLimiter,
            pinHasher,
            jwtGen,
            refreshSvc);

        return new TestApiInstance(
            instanceName,
            dbContext,
            redisProvider,
            bootstrapGateway,
            userLookupGateway,
            platformSessionStore,
            loginRateLimiter,
            terminalEnrollmentStore,
            terminalPinRateLimiter,
            tokenValidator,
            sessionManager,
            authService,
            terminalService,
            pinAuthService,
            passwordHasher);
    }

    [Fact]
    public async Task LoginOnInstanceA_RefreshOnInstanceB_SucceedsWithAtomicRotation()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var tenantSlug = $"mi-{Guid.NewGuid():N}"[..12];
        var email = $"multi.{Guid.NewGuid():N}@example.com";
        var password = "SecurePassword123!";
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(email, passwordHasher.HashPassword(password).Hash, now);
            adminCtx.Users.Add(user);
            var tenant = Tenant.Create("Multi Instance Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);
            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.Add(membership);
            await adminCtx.SaveChangesAsync();
        }

        await using var instanceA = await CreateInstanceAsync("InstanceA");
        await using var instanceB = await CreateInstanceAsync("InstanceB");

        // 1. Login on Instance A
        var loginResult = await instanceA.AuthService.LoginAsync(new LoginCommand(email, password, tenantSlug));
        Assert.NotNull(loginResult.RefreshToken);

        // 2. Refresh on Instance B
        var refreshResult = await instanceB.AuthService.RefreshSessionAsync(loginResult.RefreshToken, "127.0.0.1", "InstanceB-Agent");
        Assert.NotNull(refreshResult.AccessToken);
        Assert.NotNull(refreshResult.RefreshToken);
        Assert.NotEqual(loginResult.RefreshToken, refreshResult.RefreshToken);

        // 3. Attempting to use old refresh token on Instance A triggers reuse detection
        await Assert.ThrowsAsync<AuthFailureException>(() =>
            instanceA.AuthService.RefreshSessionAsync(loginResult.RefreshToken, "127.0.0.1", "InstanceA-Agent"));

        // 4. Family is now revoked: Even the new token from Instance B is revoked
        await Assert.ThrowsAsync<AuthFailureException>(() =>
            instanceB.AuthService.RefreshSessionAsync(refreshResult.RefreshToken, "127.0.0.1", "InstanceB-Agent"));
    }

    [Fact]
    public async Task TerminalEnrollment_OnInstanceA_ActivateOnInstanceB_SecondActivationFails()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var email = $"posadmin.{Guid.NewGuid():N}@example.com";
        var passwordHasher = new AspNetCorePasswordHasher();
        UserId adminUserId;

        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(email, passwordHasher.HashPassword("Pass123!").Hash, now);
            adminCtx.Users.Add(user);
            var tenant = Tenant.Create("Enroll Tenant", $"et-{Guid.NewGuid():N}"[..12], tenantId);
            adminCtx.Tenants.Add(tenant);
            var brand = Brand.Create(tenantId, "Enroll Brand", $"eb-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);
            var branch = Branch.Create(tenantId, brand, "Enroll Branch", $"ebr-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);
            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.Add(membership);
            await adminCtx.SaveChangesAsync();
            adminUserId = user.Id;
        }

        await using var instanceA = await CreateInstanceAsync("InstanceA");
        await using var instanceB = await CreateInstanceAsync("InstanceB");

        // 1. Instance A generates single-use enrollment code stored in Redis
        var enroll = await instanceA.TerminalService.CreateEnrollmentCodeAsync(
            tenantId,
            new EnrollTerminalCommand(branchId.Value, "POS-TERM-1", "device-hw-888"),
            adminUserId);
        Assert.NotNull(enroll.EnrollmentCode);

        // 2. Instance B activates terminal using atomic GETDEL
        var activate = await instanceB.TerminalService.ActivateTerminalAsync(new ActivateTerminalCommand(
            EnrollmentCode: enroll.EnrollmentCode,
            TerminalName: "POS-TERM-1",
            DeviceIdentifier: "device-hw-888"));
        Assert.NotNull(activate.DeviceSecret);

        // 3. Second activation attempt on Instance A fails because ticket was atomically deleted
        await Assert.ThrowsAsync<PinAuthFailureException>(() =>
            instanceA.TerminalService.ActivateTerminalAsync(new ActivateTerminalCommand(
                EnrollmentCode: enroll.EnrollmentCode,
                TerminalName: "POS-TERM-1",
                DeviceIdentifier: "device-hw-888")));
    }

    [Fact]
    public async Task TerminalPin_ProgressiveDelayAndLockout_EnforcedAcrossInstances()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        await using var instanceA = await CreateInstanceAsync("InstanceA");
        await using var instanceB = await CreateInstanceAsync("InstanceB");

        var terminalId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Instance A records 3 failed attempts
        await instanceA.PinRateLimiter.RecordFailedAttemptAsync(terminalId, userId);
        await instanceA.PinRateLimiter.RecordFailedAttemptAsync(terminalId, userId);
        await instanceA.PinRateLimiter.RecordFailedAttemptAsync(terminalId, userId);

        // Instance B observes progressive delay
        var checkB = await instanceB.PinRateLimiter.CheckAttemptAllowedAsync(terminalId, userId);
        Assert.False(checkB.IsLockedOut);
        Assert.True(checkB.BackoffDelay.HasValue && checkB.BackoffDelay.Value > TimeSpan.Zero);

        // Instance A records 2 more failed attempts (5 total)
        await instanceA.PinRateLimiter.RecordFailedAttemptAsync(terminalId, userId);
        await instanceA.PinRateLimiter.RecordFailedAttemptAsync(terminalId, userId);

        // Instance B observes hard lockout
        var lockoutB = await instanceB.PinRateLimiter.CheckAttemptAllowedAsync(terminalId, userId);
        Assert.True(lockoutB.IsLockedOut);
        Assert.True(lockoutB.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task LogoutAll_ImmediatelyInvalidatesTokens_AcrossInstances()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var tenantSlug = $"la-{Guid.NewGuid():N}"[..12];
        var email = $"logoutall.{Guid.NewGuid():N}@example.com";
        var password = "SecurePassword123!";
        var passwordHasher = new AspNetCorePasswordHasher();
        UserId userId;

        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(email, passwordHasher.HashPassword(password).Hash, now);
            adminCtx.Users.Add(user);
            var tenant = Tenant.Create("LogoutAll Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);
            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.Add(membership);
            await adminCtx.SaveChangesAsync();
            userId = user.Id;
        }

        await using var instanceA = await CreateInstanceAsync("InstanceA");
        await using var instanceB = await CreateInstanceAsync("InstanceB");

        var login = await instanceA.AuthService.LoginAsync(new LoginCommand(email, password, tenantSlug));
        var sessionId = login.Session.Id;

        // Instance B validates token session is active
        var isValidBefore = await instanceB.TokenValidator.ValidateTokenActiveAsync(sessionId, userId.Value, 1);
        Assert.True(isValidBefore);

        // Instance A performs LogoutAll
        await instanceA.SessionManager.LogoutAllAsync(userId);

        // Instance B immediately sees the token as invalidated (security_version mismatch)
        var isValidAfterB = await instanceB.TokenValidator.ValidateTokenActiveAsync(sessionId, userId.Value, 1);
        Assert.False(isValidAfterB);

        // Instance A also sees it as invalidated
        var isValidAfterA = await instanceA.TokenValidator.ValidateTokenActiveAsync(sessionId, userId.Value, 1);
        Assert.False(isValidAfterA);
    }

    [Fact]
    public async Task ConcurrentRefreshRaceCondition_OneSucceeds_OneFailsWithReuse()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var tenantSlug = $"rt-{Guid.NewGuid():N}"[..12];
        var email = $"race.{Guid.NewGuid():N}@example.com";
        var password = "SecurePassword123!";
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(email, passwordHasher.HashPassword(password).Hash, now);
            adminCtx.Users.Add(user);
            var tenant = Tenant.Create("Race Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);
            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.Add(membership);
            await adminCtx.SaveChangesAsync();
        }

        await using var instanceA = await CreateInstanceAsync("InstanceA");
        await using var instanceB = await CreateInstanceAsync("InstanceB");

        var login = await instanceA.AuthService.LoginAsync(new LoginCommand(email, password, tenantSlug));
        var refreshToken = login.RefreshToken;

        // Concurrently refresh using the same refresh token on Instance A and Instance B
        var taskA = Task.Run(async () =>
        {
            try { return (Success: true, Result: await instanceA.AuthService.RefreshSessionAsync(refreshToken, "127.0.0.1", "AgentA"), Error: (Exception?)null); }
            catch (Exception ex) { return (Success: false, Result: (AuthResult?)null, Error: ex); }
        });

        var taskB = Task.Run(async () =>
        {
            try { return (Success: true, Result: await instanceB.AuthService.RefreshSessionAsync(refreshToken, "127.0.0.1", "AgentB"), Error: (Exception?)null); }
            catch (Exception ex) { return (Success: false, Result: (AuthResult?)null, Error: ex); }
        });

        var results = await Task.WhenAll(taskA, taskB);
        var successes = results.Count(r => r.Success);
        var failures = results.Count(r => !r.Success);

        // Exactly one request must succeed, and the other must fail due to row-level lock & reuse detection
        Assert.Equal(1, successes);
        Assert.Equal(1, failures);
        Assert.IsType<AuthFailureException>(results.First(r => !r.Success).Error);
    }

    [Fact]
    public async Task RestartScenario_PlatformAndTenantSessions_PersistedInPostgreSql()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var superAdminId = UserId.New();
        var tenantId = TenantId.New();
        var tenantSlug = $"rst-{Guid.NewGuid():N}"[..12];
        var tenantEmail = $"restart.{Guid.NewGuid():N}@example.com";
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var superAdmin = User.Create($"super.{Guid.NewGuid():N}@example.com", passwordHasher.HashPassword("AdminPass123!").Hash, now, id: superAdminId);
            var tenantUser = User.Create(tenantEmail, passwordHasher.HashPassword("TenantPass123!").Hash, now);
            adminCtx.Users.AddRange(superAdmin, tenantUser);

            var tenant = Tenant.Create("Restart Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);
            var membership = UserMembership.Create(tenantId, tenantUser.Id, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.Add(membership);

            await adminCtx.SaveChangesAsync();
        }

        string rawTenantRefreshToken;
        string rawPlatformRefreshToken;

        // Instance 1 creates sessions and then terminates (disposing in-memory state)
        await using (var instance1 = await CreateInstanceAsync("Instance1"))
        {
            // 1. Tenant login
            var tenantLogin = await instance1.AuthService.LoginAsync(new LoginCommand(tenantEmail, "TenantPass123!", tenantSlug));
            rawTenantRefreshToken = tenantLogin.RefreshToken;

            // 2. SuperAdmin platform session
            var pSessionId = Guid.NewGuid();
            var pFamilyId = Guid.NewGuid();
            var rawToken = new RefreshTokenService().GenerateOpaqueToken();
            var tokenHash = new RefreshTokenService().HashToken(rawToken);
            rawPlatformRefreshToken = rawToken;

            await instance1.PlatformSessionStore.CreateSessionAsync(
                superAdminId,
                pSessionId,
                pFamilyId,
                tokenHash,
                TimeSpan.FromDays(7),
                now);
        }

        // Instance 2 boots up fresh from PostgreSQL
        await using (var instance2 = await CreateInstanceAsync("Instance2"))
        {
            // Platform session persists in iam.platform_sessions and can rotate
            var platformRefresh = await instance2.AuthService.RefreshSessionAsync(rawPlatformRefreshToken, "127.0.0.1", "FreshInstance");
            Assert.NotNull(platformRefresh.AccessToken);

            // Tenant session persists in iam.sessions and can rotate
            var tenantRefresh = await instance2.AuthService.RefreshSessionAsync(rawTenantRefreshToken, "127.0.0.1", "FreshInstance");
            Assert.NotNull(tenantRefresh.AccessToken);
        }
    }
}
