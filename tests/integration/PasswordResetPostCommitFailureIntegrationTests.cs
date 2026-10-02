using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
/// Integration tests verifying the post-commit resilience of PasswordResetAsync:
/// When Redis cache invalidation or platform session store cleanup throws an exception,
/// the already-committed PostgreSQL transaction is never rolled back, no misleading
/// 500 error is returned to the user, and all security guarantees (password update,
/// security version increment, relational and platform session revocations) are maintained.
/// </summary>
public class PasswordResetPostCommitFailureIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PasswordResetPostCommitFailureIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private static string ComputeSha256(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
    public async Task ResetPasswordAsync_WhenCacheInvalidationThrows_SucceedsWithoutRollbackAndPersistsChanges()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var tenantSlug = $"tx-cache-err-{Guid.NewGuid():N}"[..10];
        var email = $"pwd.cache.err.{Guid.NewGuid():N}@restaurant.test";
        var passwordHasher = new AspNetCorePasswordHasher();
        var initialPasswordHash = passwordHasher.HashPassword("InitialPass123!").Hash;

        UserId userId;
        var sessionId = Guid.NewGuid();

        // 1. Seed tenant, branch, user, active session and refresh token
        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(email, initialPasswordHash, now);
            adminCtx.Users.Add(user);

            var tenant = Tenant.Create("Tx Cache Err Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = RestaurantOrder.Domain.Brands.Brand.Create(tenantId, "Tx Brand Cache", $"tbc-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var branch = Branch.Create(tenantId, brand, "Tx Branch Cache", $"tbchc-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);

            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.Waiter, branchId, now);
            adminCtx.Memberships.Add(membership);

            var sess = AuthSession.Create(tenantId, user.Id, membership.Id, branchId, AuthenticationMethod.Password, TimeSpan.FromHours(8), now, "127.0.0.1", "agent-1", sessionId);
            adminCtx.Sessions.Add(sess);

            var rt = RefreshToken.Create(tenantId, sess.Id, Guid.NewGuid(), ComputeSha256("rt_cache_err_token"), TimeSpan.FromDays(30), now);
            adminCtx.RefreshTokens.Add(rt);

            await adminCtx.SaveChangesAsync();
            userId = user.Id;
        }

        // 2. Setup service dependencies with a fake ITokenRevocationValidator that throws on cache invalidation
        await using var runtimeCtx = await CreateRuntimeDbContextAsync(new TenantContext(tenantId));
        var bootstrapGateway = new PostgreSqlIamBootstrapGateway(runtimeCtx);
        var userLookupGateway = new PostgreSqlIamUserLookupGateway(runtimeCtx);
        var platformSessionStore = new PostgreSqlPlatformSessionStore(runtimeCtx);

        var mockTokenValidator = new Mock<ITokenRevocationValidator>();
        mockTokenValidator
            .Setup(v => v.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated Redis timeout during user cache invalidation"));
        mockTokenValidator
            .Setup(v => v.InvalidateSessionCacheAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated Redis timeout during session cache invalidation"));

        var sessionManager = new AuthSessionManager(platformSessionStore, bootstrapGateway, mockTokenValidator.Object);
        var notificationSender = new TestSinkIdentityNotificationSender(NullLogger<TestSinkIdentityNotificationSender>.Instance);

        var identityService = new StaffIdentityService(
            runtimeCtx,
            bootstrapGateway,
            userLookupGateway,
            passwordHasher,
            notificationSender,
            sessionManager);

        // 3. Request password reset
        var rawResetToken = await identityService.RequestPasswordResetAsync(
            new RequestPasswordResetCommand(email, tenantSlug));
        Assert.NotNull(rawResetToken);

        // 4. Execute password reset: MUST NOT throw despite Redis cache invalidation failure
        const string newPassword = "NewSecurePasswordAfterCacheErr456!";
        var ex = await Record.ExceptionAsync(() =>
            identityService.ResetPasswordAsync(new ResetPasswordCommand(rawResetToken!, newPassword)));
        Assert.Null(ex);

        // 5. Verify database changes were fully committed (not rolled back)
        await using (var verifyCtx = CreateAdminDbContext())
        {
            var verifiedUser = await verifyCtx.Users.FindAsync(userId);
            Assert.NotNull(verifiedUser);
            Assert.Equal(2, verifiedUser.SecurityVersion);
            Assert.NotEqual(initialPasswordHash, verifiedUser.PasswordHash);
            Assert.Equal(PasswordVerificationResult.Success, passwordHasher.VerifyPassword(newPassword, verifiedUser.PasswordHash));
            Assert.Equal(PasswordVerificationResult.Failed, passwordHasher.VerifyPassword("InitialPass123!", verifiedUser.PasswordHash));

            var sess = await verifyCtx.Sessions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionId);
            Assert.NotNull(sess);
            Assert.True(sess.IsRevoked);
            Assert.NotNull(sess.RevokedAtUtc);
            Assert.Equal("password_reset", sess.RevocationReason);

            var rt = await verifyCtx.RefreshTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.SessionId == sessionId);
            Assert.NotNull(rt);
            Assert.True(rt.IsRevoked);
            Assert.NotNull(rt.RevokedAtUtc);

            var tokenHash = ComputeSha256(rawResetToken!);
            var dbToken = await verifyCtx.PasswordResetTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
            Assert.NotNull(dbToken);
            Assert.True(dbToken.IsConsumed);
            Assert.NotNull(dbToken.ConsumedAtUtc);

            var audit = await verifyCtx.SecurityAuditEvents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.UserId == userId && a.EventType == SecurityAuditEventType.PasswordChanged);
            Assert.NotNull(audit);
        }

        // 6. Verify second attempt with the same consumed token is rejected
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            identityService.ResetPasswordAsync(new ResetPasswordCommand(rawResetToken!, "AnotherPassword789!")));
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenPlatformSessionStoreThrows_SucceedsWithoutRollbackAndPreservesSecurity()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var tenantSlug = $"tx-plat-err-{Guid.NewGuid():N}"[..10];
        var email = $"pwd.plat.err.{Guid.NewGuid():N}@restaurant.test";
        var passwordHasher = new AspNetCorePasswordHasher();
        var initialPasswordHash = passwordHasher.HashPassword("InitialPass123!").Hash;

        UserId userId;
        var sessionId = Guid.NewGuid();

        // 1. Seed tenant, user, and session
        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(email, initialPasswordHash, now);
            adminCtx.Users.Add(user);

            var tenant = Tenant.Create("Tx Plat Err Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = RestaurantOrder.Domain.Brands.Brand.Create(tenantId, "Tx Brand Plat", $"tbp-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var branch = Branch.Create(tenantId, brand, "Tx Branch Plat", $"tbchp-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);

            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.Waiter, branchId, now);
            adminCtx.Memberships.Add(membership);

            var sess = AuthSession.Create(tenantId, user.Id, membership.Id, branchId, AuthenticationMethod.Password, TimeSpan.FromHours(8), now, "127.0.0.1", "agent-1", sessionId);
            adminCtx.Sessions.Add(sess);

            var rt = RefreshToken.Create(tenantId, sess.Id, Guid.NewGuid(), ComputeSha256("rt_plat_err_token"), TimeSpan.FromDays(30), now);
            adminCtx.RefreshTokens.Add(rt);

            await adminCtx.SaveChangesAsync();
            userId = user.Id;
        }

        // 2. Setup service dependencies with mock IPlatformSessionStore that throws on RevokeAllUserSessionsAsync
        await using var runtimeCtx = await CreateRuntimeDbContextAsync(new TenantContext(tenantId));
        var bootstrapGateway = new PostgreSqlIamBootstrapGateway(runtimeCtx);
        var userLookupGateway = new PostgreSqlIamUserLookupGateway(runtimeCtx);

        var mockPlatformStore = new Mock<IPlatformSessionStore>();
        mockPlatformStore
            .Setup(p => p.RevokeAllUserSessionsAsync(It.IsAny<UserId>(), It.IsAny<DateTimeOffset>()))
            .ThrowsAsync(new InvalidOperationException("Simulated platform store connection failure"));

        var sessionManager = new AuthSessionManager(mockPlatformStore.Object, bootstrapGateway);
        var notificationSender = new TestSinkIdentityNotificationSender(NullLogger<TestSinkIdentityNotificationSender>.Instance);

        var identityService = new StaffIdentityService(
            runtimeCtx,
            bootstrapGateway,
            userLookupGateway,
            passwordHasher,
            notificationSender,
            sessionManager);

        // 3. Request password reset
        var rawResetToken = await identityService.RequestPasswordResetAsync(
            new RequestPasswordResetCommand(email, tenantSlug));
        Assert.NotNull(rawResetToken);

        // 4. Execute password reset: MUST NOT throw or roll back
        const string newPassword = "NewSecurePasswordPlatErr456!";
        var ex = await Record.ExceptionAsync(() =>
            identityService.ResetPasswordAsync(new ResetPasswordCommand(rawResetToken!, newPassword)));
        Assert.Null(ex);

        // 5. Verify database state: changes are committed and security guarantees preserved
        await using (var verifyCtx = CreateAdminDbContext())
        {
            var verifiedUser = await verifyCtx.Users.FindAsync(userId);
            Assert.NotNull(verifiedUser);
            Assert.Equal(2, verifiedUser.SecurityVersion);
            Assert.NotEqual(initialPasswordHash, verifiedUser.PasswordHash);
            Assert.Equal(PasswordVerificationResult.Success, passwordHasher.VerifyPassword(newPassword, verifiedUser.PasswordHash));

            var sess = await verifyCtx.Sessions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionId);
            Assert.NotNull(sess);
            Assert.True(sess.IsRevoked);

            var rt = await verifyCtx.RefreshTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.SessionId == sessionId);
            Assert.NotNull(rt);
            Assert.True(rt.IsRevoked);

            var tokenHash = ComputeSha256(rawResetToken!);
            var dbToken = await verifyCtx.PasswordResetTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
            Assert.NotNull(dbToken);
            Assert.True(dbToken.IsConsumed);
        }
    }
}
