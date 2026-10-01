using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

public class PasswordResetTransactionalRevocationIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PasswordResetTransactionalRevocationIntegrationTests(TestcontainersFixture fixture)
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
    public async Task ResetPasswordAsync_RevokesAllSessionsAndRefreshTokensInSameTransaction()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var tenantSlug = $"tx-rst-{Guid.NewGuid():N}"[..10];
        var email = $"pwd.reset.{Guid.NewGuid():N}@restaurant.test";
        var passwordHasher = new AspNetCorePasswordHasher();
        var initialPasswordHash = passwordHasher.HashPassword("InitialPass123!").Hash;

        UserId userId;
        var session1Id = Guid.NewGuid();
        var session2Id = Guid.NewGuid();

        // 1. Seed tenant, branch, user, memberships, active sessions and refresh tokens
        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(email, initialPasswordHash, now);
            adminCtx.Users.Add(user);

            var tenant = Tenant.Create("Tx Reset Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = RestaurantOrder.Domain.Brands.Brand.Create(tenantId, "Tx Brand", $"tbr-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var branch = Branch.Create(tenantId, brand, "Tx Branch", $"tbrch-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);

            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.Waiter, branchId, now);
            adminCtx.Memberships.Add(membership);

            var sess1 = AuthSession.Create(tenantId, user.Id, membership.Id, branchId, AuthenticationMethod.Password, TimeSpan.FromHours(8), now, "127.0.0.1", "agent-1", session1Id);
            var sess2 = AuthSession.Create(tenantId, user.Id, membership.Id, branchId, AuthenticationMethod.Password, TimeSpan.FromHours(8), now, "127.0.0.1", "agent-2", session2Id);
            adminCtx.Sessions.AddRange(sess1, sess2);

            var rt1 = RefreshToken.Create(tenantId, sess1.Id, Guid.NewGuid(), ComputeSha256("rt1_token"), TimeSpan.FromDays(30), now);
            var rt2 = RefreshToken.Create(tenantId, sess2.Id, Guid.NewGuid(), ComputeSha256("rt2_token"), TimeSpan.FromDays(30), now);
            adminCtx.RefreshTokens.AddRange(rt1, rt2);

            await adminCtx.SaveChangesAsync();
            userId = user.Id;
        }

        // 2. Setup service dependencies
        await using var runtimeCtx = await CreateRuntimeDbContextAsync(new TenantContext(tenantId));
        var bootstrapGateway = new PostgreSqlIamBootstrapGateway(runtimeCtx);
        var userLookupGateway = new PostgreSqlIamUserLookupGateway(runtimeCtx);
        var platformSessionStore = new PostgreSqlPlatformSessionStore(runtimeCtx);
        var sessionManager = new AuthSessionManager(platformSessionStore, bootstrapGateway);
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

        // 4. Execute password reset
        const string newPassword = "NewSecurePassword456!";
        await identityService.ResetPasswordAsync(new ResetPasswordCommand(rawResetToken!, newPassword));

        // 5. Verify database state
        await using (var verifyCtx = CreateAdminDbContext())
        {
            var verifiedUser = await verifyCtx.Users.FindAsync(userId);
            Assert.NotNull(verifiedUser);
            Assert.Equal(2, verifiedUser.SecurityVersion);
            Assert.NotEqual(initialPasswordHash, verifiedUser.PasswordHash);
            Assert.Equal(PasswordVerificationResult.Success, passwordHasher.VerifyPassword(newPassword, verifiedUser.PasswordHash));

            var sessions = await verifyCtx.Sessions
                .IgnoreQueryFilters()
                .Where(s => s.UserId == userId)
                .ToListAsync();

            Assert.Equal(2, sessions.Count);
            foreach (var sess in sessions)
            {
                Assert.True(sess.IsRevoked);
                Assert.NotNull(sess.RevokedAtUtc);
                Assert.Equal("password_reset", sess.RevocationReason);
            }

            var refreshTokens = await verifyCtx.RefreshTokens
                .IgnoreQueryFilters()
                .Where(rt => rt.SessionId == session1Id || rt.SessionId == session2Id)
                .ToListAsync();

            Assert.Equal(2, refreshTokens.Count);
            foreach (var rt in refreshTokens)
            {
                Assert.True(rt.IsRevoked);
                Assert.NotNull(rt.RevokedAtUtc);
            }

            var tokenHash = ComputeSha256(rawResetToken!);
            var dbToken = await verifyCtx.PasswordResetTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            Assert.NotNull(dbToken);
            Assert.True(dbToken.IsConsumed);
            Assert.NotNull(dbToken.ConsumedAtUtc);
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenInvalidTokenProvided_DoesNotRevokeSessionsOrChangePassword()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var tenantSlug = $"tx-fail-{Guid.NewGuid():N}"[..10];
        var email = $"pwd.fail.{Guid.NewGuid():N}@restaurant.test";
        var passwordHasher = new AspNetCorePasswordHasher();
        var initialPasswordHash = passwordHasher.HashPassword("InitialPass123!").Hash;

        UserId userId;
        var sessionId = Guid.NewGuid();

        // 1. Seed tenant and active user session
        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(email, initialPasswordHash, now);
            adminCtx.Users.Add(user);

            var tenant = Tenant.Create("Tx Fail Tenant", tenantSlug, tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = RestaurantOrder.Domain.Brands.Brand.Create(tenantId, "Tx Brand Fail", $"tbf-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var branch = Branch.Create(tenantId, brand, "Tx Branch Fail", $"tbfch-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);

            var membership = UserMembership.Create(tenantId, user.Id, AuthRole.Waiter, branchId, now);
            adminCtx.Memberships.Add(membership);

            var sess = AuthSession.Create(tenantId, user.Id, membership.Id, branchId, AuthenticationMethod.Password, TimeSpan.FromHours(8), now, "127.0.0.1", "agent-1", sessionId);
            adminCtx.Sessions.Add(sess);

            var rt = RefreshToken.Create(tenantId, sess.Id, Guid.NewGuid(), ComputeSha256("rt_fail_token"), TimeSpan.FromDays(30), now);
            adminCtx.RefreshTokens.Add(rt);

            await adminCtx.SaveChangesAsync();
            userId = user.Id;
        }

        // 2. Setup service dependencies
        await using var runtimeCtx = await CreateRuntimeDbContextAsync(new TenantContext(tenantId));
        var bootstrapGateway = new PostgreSqlIamBootstrapGateway(runtimeCtx);
        var userLookupGateway = new PostgreSqlIamUserLookupGateway(runtimeCtx);
        var platformSessionStore = new PostgreSqlPlatformSessionStore(runtimeCtx);
        var sessionManager = new AuthSessionManager(platformSessionStore, bootstrapGateway);
        var notificationSender = new TestSinkIdentityNotificationSender(NullLogger<TestSinkIdentityNotificationSender>.Instance);

        var identityService = new StaffIdentityService(
            runtimeCtx,
            bootstrapGateway,
            userLookupGateway,
            passwordHasher,
            notificationSender,
            sessionManager);

        // 3. Attempt reset with non-existent token -> throws InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            identityService.ResetPasswordAsync(new ResetPasswordCommand("non_existent_token_hex_value", "NewSecurePassword456!")));

        // 4. Verify state was NOT changed
        await using (var verifyCtx = CreateAdminDbContext())
        {
            var verifiedUser = await verifyCtx.Users.FindAsync(userId);
            Assert.NotNull(verifiedUser);
            Assert.Equal(1, verifiedUser.SecurityVersion);
            Assert.Equal(initialPasswordHash, verifiedUser.PasswordHash);

            var sess = await verifyCtx.Sessions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionId);
            Assert.NotNull(sess);
            Assert.False(sess.IsRevoked);
            Assert.Null(sess.RevokedAtUtc);
            Assert.Null(sess.RevocationReason);

            var rt = await verifyCtx.RefreshTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.SessionId == sessionId);

            Assert.NotNull(rt);
            Assert.False(rt.IsRevoked);
            Assert.Null(rt.RevokedAtUtc);
        }
    }
}
