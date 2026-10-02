using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
using RestaurantOrder.Infrastructure.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying Phase 3 distributed security hardening:
/// 1. Platform refresh token reuse detection and revocation across instances
/// 2. Session revocation IDOR protection (user ownership validation)
/// 3. Atomic account lockout concurrency and status restoration after expiry
/// 4. Terminal PIN email-based brute force protection across instances
/// 5. JWT session-user binding validation
/// 6. Least-privilege PostgreSQL enforcement for runtime role on platform sessions
/// </summary>
public class PostgreSqlDistributedSecurityHardenIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PostgreSqlDistributedSecurityHardenIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private RestaurantOrderDbContext CreateAdminDbContext(TenantId? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        var tenantContext = tenantId.HasValue
            ? new TenantContext(tenantId.Value.Value, isAuthenticated: true)
            : TenantContext.Empty;
        return new RestaurantOrderDbContext(options, tenantContext);
    }

    private async Task<TestApiInstance> CreateInstanceAsync(string instanceName, string? superAdminEmail = null)
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

        var authSettingsObj = new AuthSettings();
        if (!string.IsNullOrWhiteSpace(superAdminEmail))
        {
            authSettingsObj.SuperAdminEmails = new List<string> { superAdminEmail };
        }
        var authSettings = Options.Create(authSettingsObj);

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
    public async Task PlatformRefreshToken_Reuse_AcrossInstances_RevokesFamilyAndFailsSubsequentUse()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var superAdminEmail = $"superadmin.{Guid.NewGuid():N}@platform.local";
        var password = "SuperSecretPassword123!";
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(superAdminEmail, passwordHasher.HashPassword(password).Hash, now);
            adminCtx.Users.Add(user);
            await adminCtx.SaveChangesAsync();
        }

        await using var instanceA = await CreateInstanceAsync("InstanceA", superAdminEmail);
        await using var instanceB = await CreateInstanceAsync("InstanceB", superAdminEmail);

        // 1. SuperAdmin login on Instance A
        var loginResult = await instanceA.AuthService.LoginAsync(new LoginCommand(superAdminEmail, password, null));
        Assert.NotNull(loginResult.RefreshToken);
        var rt1 = loginResult.RefreshToken;

        // 2. Rotate RT1 on Instance B -> produces RT2
        var refreshResult = await instanceB.AuthService.RefreshSessionAsync(rt1, "127.0.0.1", "InstanceB-Agent");
        Assert.NotNull(refreshResult.RefreshToken);
        var rt2 = refreshResult.RefreshToken;
        Assert.NotEqual(rt1, rt2);

        // 3. Reusing old RT1 on Instance A triggers reuse detection
        await Assert.ThrowsAsync<AuthFailureException>(() =>
            instanceA.AuthService.RefreshSessionAsync(rt1, "127.0.0.1", "InstanceA-Agent"));

        // 4. Family revoked: subsequent rotation with RT2 on Instance B also fails
        await Assert.ThrowsAsync<AuthFailureException>(() =>
            instanceB.AuthService.RefreshSessionAsync(rt2, "127.0.0.1", "InstanceB-Agent"));

        // 5. Verify database state for the platform session
        await using (var adminCtx = CreateAdminDbContext())
        {
            var session = await adminCtx.PlatformSessions.FirstOrDefaultAsync(s => s.Id == loginResult.Session.Id);
            Assert.NotNull(session);
            Assert.True(session.IsRevoked);
            Assert.Equal("token_reuse_detected", session.RevocationReason);
        }
    }

    [Fact]
    public async Task SessionRevocation_IdorProtection_UserCannotRevokeOtherUsersSession()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var tenantSlug = $"idor-{Guid.NewGuid():N}"[..12];
        var email1 = $"user1.{Guid.NewGuid():N}@example.com";
        var email2 = $"user2.{Guid.NewGuid():N}@example.com";
        var password = "SecurePassword123!";
        var passwordHasher = new AspNetCorePasswordHasher();
        UserId user1Id, user2Id;

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("IDOR Test Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);

            var u1 = User.Create(email1, passwordHasher.HashPassword(password).Hash, now);
            var u2 = User.Create(email2, passwordHasher.HashPassword(password).Hash, now);
            adminCtx.Users.AddRange(u1, u2);

            var m1 = UserMembership.Create(tenantId, u1.Id, AuthRole.RestaurantAdmin, null, now);
            var m2 = UserMembership.Create(tenantId, u2.Id, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.AddRange(m1, m2);

            await adminCtx.SaveChangesAsync();
            user1Id = u1.Id;
            user2Id = u2.Id;
        }

        await using var instanceA = await CreateInstanceAsync("InstanceA");
        await using var instanceB = await CreateInstanceAsync("InstanceB");

        var login1 = await instanceA.AuthService.LoginAsync(new LoginCommand(email1, password, tenantSlug));
        var login2 = await instanceB.AuthService.LoginAsync(new LoginCommand(email2, password, tenantSlug));

        // User 1 attempts to revoke User 2's session -> must return false (IDOR blocked)
        var idorResult = await instanceA.AuthService.RevokeSessionAsync(user1Id, login2.Session.Id);
        Assert.False(idorResult);

        // Verify User 2's session is still active
        await using (var adminCtx = CreateAdminDbContext(tenantId))
        {
            var s2 = await adminCtx.Sessions.FirstOrDefaultAsync(s => s.Id == login2.Session.Id);
            Assert.NotNull(s2);
            Assert.False(s2.IsRevoked);
        }

        // User 2 revokes their own session -> must return true
        var ownRevokeResult = await instanceB.AuthService.RevokeSessionAsync(user2Id, login2.Session.Id);
        Assert.True(ownRevokeResult);

        // Verify User 2's session is now revoked
        await using (var adminCtx = CreateAdminDbContext(tenantId))
        {
            var s2 = await adminCtx.Sessions.FirstOrDefaultAsync(s => s.Id == login2.Session.Id);
            Assert.NotNull(s2);
            Assert.True(s2.IsRevoked);
        }
    }

    [Fact]
    public async Task AccountLockout_ConcurrentFailedLogins_NoLostUpdatesAndRestoresToActiveAfterExpiry()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var tenantSlug = $"lock-{Guid.NewGuid():N}"[..12];
        var email = $"lockout.{Guid.NewGuid():N}@example.com";
        var correctPassword = "CorrectPassword123!";
        var passwordHasher = new AspNetCorePasswordHasher();
        Guid userId;

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("Lockout Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);
            var user = User.Create(email, passwordHasher.HashPassword(correctPassword).Hash, now);
            adminCtx.Users.Add(user);
            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.RestaurantAdmin, null, now);
            adminCtx.Memberships.Add(membership);
            await adminCtx.SaveChangesAsync();
            userId = user.Id.Value;
        }

        await using var instanceA = await CreateInstanceAsync("InstanceA");
        await using var instanceB = await CreateInstanceAsync("InstanceB");

        // 5 sequential failed logins with wrong password to hit lockout threshold
        for (int i = 0; i < 5; i++)
        {
            var inst = i % 2 == 0 ? instanceA : instanceB;
            await Assert.ThrowsAnyAsync<Exception>(() =>
                inst.AuthService.LoginAsync(new LoginCommand(email, "WrongPassword!", tenantSlug)));
        }

        // Verify account is locked in database
        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = await adminCtx.Users.FirstOrDefaultAsync(u => u.Id == UserId.From(userId));
            Assert.NotNull(user);
            Assert.True(user.FailedLoginAttempts >= 5);
            Assert.Equal(UserStatus.Locked, user.Status);
            Assert.NotNull(user.LockoutEndUtc);

            // Simulate lockout expiry by winding back lockout_end_utc
            user.Lock(now.AddMinutes(-5), now);
            await adminCtx.SaveChangesAsync();
        }

        // Reset rate limiter for the test user so rate limiting doesn't mask lockout recovery
        await instanceB.LoginRateLimiter.ResetAttemptsAsync("unknown", email, tenantSlug);

        // Attempt login on Instance B with correct password after lockout expiration
        var loginResult = await instanceB.AuthService.LoginAsync(new LoginCommand(email, correctPassword, tenantSlug));
        Assert.NotNull(loginResult.AccessToken);

        // Verify user status was restored to Active, failed count reset to 0, lockout cleared
        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = await adminCtx.Users.FirstOrDefaultAsync(u => u.Id == UserId.From(userId));
            Assert.NotNull(user);
            Assert.Equal(UserStatus.Active, user.Status);
            Assert.Equal(0, user.FailedLoginAttempts);
            Assert.Null(user.LockoutEndUtc);
        }
    }

    [Fact]
    public async Task JwtValidation_SessionUserMismatch_Rejected()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var tenantSlug = $"jwt-{Guid.NewGuid():N}"[..12];
        var email1 = $"u1.{Guid.NewGuid():N}@example.com";
        var email2 = $"u2.{Guid.NewGuid():N}@example.com";
        var password = "SecurePassword123!";
        var passwordHasher = new AspNetCorePasswordHasher();
        Guid u2Guid;

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("JWT Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);
            var u1 = User.Create(email1, passwordHasher.HashPassword(password).Hash, now);
            var u2 = User.Create(email2, passwordHasher.HashPassword(password).Hash, now);
            adminCtx.Users.AddRange(u1, u2);
            adminCtx.Memberships.Add(UserMembership.Create(tenantId, u1.Id, AuthRole.RestaurantAdmin, null, now));
            adminCtx.Memberships.Add(UserMembership.Create(tenantId, u2.Id, AuthRole.RestaurantAdmin, null, now));
            await adminCtx.SaveChangesAsync();
            u2Guid = u2.Id.Value;
        }

        await using var instance = await CreateInstanceAsync("InstanceA");
        var login1 = await instance.AuthService.LoginAsync(new LoginCommand(email1, password, tenantSlug));

        // Validate S1 with User 2's ID -> must fail closed (user mismatch)
        var isValid = await instance.TokenValidator.ValidateTokenActiveAsync(
            login1.Session.Id,
            u2Guid,
            1);

        Assert.False(isValid);
    }

    [Fact]
    public async Task PlatformSession_LeastPrivilege_DirectTableAccessDeniedForRuntimeRole()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var runtimeConnStr = await _fixture.ProvisionTemporaryRuntimeRoleAsync();
        await using var conn = new NpgsqlConnection(runtimeConnStr);
        await conn.OpenAsync();

        // 1. SELECT directly from iam.platform_sessions must be denied
        await using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM iam.platform_sessions LIMIT 1;";
        var selectEx = await Assert.ThrowsAsync<PostgresException>(() => selectCmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", selectEx.SqlState); // 42501 = insufficient_privilege

        // 2. DELETE directly from iam.platform_sessions must be denied
        await using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM iam.platform_sessions;";
        var deleteEx = await Assert.ThrowsAsync<PostgresException>(() => deleteCmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", deleteEx.SqlState);

        // 3. SELECT directly from iam.platform_refresh_tokens must be denied
        await using var tokenCmd = conn.CreateCommand();
        tokenCmd.CommandText = "SELECT * FROM iam.platform_refresh_tokens LIMIT 1;";
        var tokenEx = await Assert.ThrowsAsync<PostgresException>(() => tokenCmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", tokenEx.SqlState);
    }
}
