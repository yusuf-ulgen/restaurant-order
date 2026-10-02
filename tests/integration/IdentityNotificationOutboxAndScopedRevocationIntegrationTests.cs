using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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

public class IdentityNotificationOutboxAndScopedRevocationIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public IdentityNotificationOutboxAndScopedRevocationIntegrationTests(TestcontainersFixture fixture)
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
    public async Task ScopedSessionRevocation_WhenStaffStatusUpdated_OnlyRevokesTargetTenantSessions()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantAId = TenantId.New();
        var tenantBId = TenantId.New();
        var branchAId = BranchId.New();
        var branchBId = BranchId.New();
        var email = $"multi.{Guid.NewGuid():N}@restaurant.test";
        var passwordHasher = new AspNetCorePasswordHasher();
        var passwordHash = passwordHasher.HashPassword("TestPass123!").Hash;

        UserId userId;
        var sessionAId = Guid.NewGuid();
        var sessionBId = Guid.NewGuid();

        // 1. Setup multi-tenant user with active sessions in both Tenant A and Tenant B
        await using (var adminCtx = CreateAdminDbContext())
        {
            var user = User.Create(email, passwordHash, now);
            adminCtx.Users.Add(user);

            var tenantA = Tenant.Create("Tenant A", $"ta-{Guid.NewGuid():N}"[..10], tenantAId);
            var tenantB = Tenant.Create("Tenant B", $"tb-{Guid.NewGuid():N}"[..10], tenantBId);
            adminCtx.Tenants.AddRange(tenantA, tenantB);

            var brandA = RestaurantOrder.Domain.Brands.Brand.Create(tenantAId, "Brand A", $"ba-{Guid.NewGuid():N}"[..10]);
            var brandB = RestaurantOrder.Domain.Brands.Brand.Create(tenantBId, "Brand B", $"bb-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.AddRange(brandA, brandB);

            var branchA = Branch.Create(tenantAId, brandA, "Branch A", $"bra-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchAId);
            var branchB = Branch.Create(tenantBId, brandB, "Branch B", $"brb-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchBId);
            adminCtx.Branches.AddRange(branchA, branchB);

            var memA = UserMembership.Create(tenantAId, user.Id, AuthRole.Waiter, branchAId, now);
            var memB = UserMembership.Create(tenantBId, user.Id, AuthRole.Waiter, branchBId, now);
            adminCtx.Memberships.AddRange(memA, memB);

            // Seed sessions directly
            var sessA = AuthSession.Create(tenantAId, user.Id, memA.Id, branchAId, AuthenticationMethod.Password, TimeSpan.FromHours(8), now, "127.0.0.1", "agent-a", sessionAId);
            var sessB = AuthSession.Create(tenantBId, user.Id, memB.Id, branchBId, AuthenticationMethod.Password, TimeSpan.FromHours(8), now, "127.0.0.1", "agent-b", sessionBId);
            adminCtx.Sessions.AddRange(sessA, sessB);

            await adminCtx.SaveChangesAsync();
            userId = user.Id;
        }

        // 2. Perform UpdateStaffStatusAsync to Suspended in Tenant A
        await using var runtimeCtx = await CreateRuntimeDbContextAsync();
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

        var adminPrincipalA = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantAId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        await identityService.UpdateStaffStatusAsync(
            tenantAId,
            new UpdateStaffStatusCommand(userId.Value, UserMembershipStatus.Suspended),
            adminPrincipalA);

        // 3. Verify: Tenant A session must be revoked; Tenant B session must remain valid; User.Status remains Active
        await using (var verifyCtx = CreateAdminDbContext())
        {
            var verifiedUser = await verifyCtx.Users.FindAsync(userId);
            Assert.NotNull(verifiedUser);
            Assert.Equal(UserStatus.Active, verifiedUser.Status); // Global user must NOT be suspended

            var memA = await verifyCtx.Memberships
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == tenantAId && m.UserId == userId);
            Assert.NotNull(memA);
            Assert.Equal(UserMembershipStatus.Suspended, memA.Status);

            var memB = await verifyCtx.Memberships
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == tenantBId && m.UserId == userId);
            Assert.NotNull(memB);
            Assert.Equal(UserMembershipStatus.Active, memB.Status);

            var sA = await verifyCtx.Sessions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionAId);
            Assert.NotNull(sA);
            Assert.True(sA.IsRevoked);
            Assert.NotNull(sA.RevokedAtUtc);

            var sB = await verifyCtx.Sessions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionBId);
            Assert.NotNull(sB);
            Assert.False(sB.IsRevoked);
            Assert.Null(sB.RevokedAtUtc);
        }
    }

    [Fact]
    public async Task IdentityNotificationOutbox_EndToEnd_EnqueueEncryptAndDispatch()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var email = $"outbox.{Guid.NewGuid():N}@restaurant.test";
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("Outbox Tenant", $"ot-{Guid.NewGuid():N}"[..10], tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = RestaurantOrder.Domain.Brands.Brand.Create(tenantId, "Outbox Brand", $"ob-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var branch = Branch.Create(tenantId, brand, "Outbox Branch", $"obr-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);

            await adminCtx.SaveChangesAsync();
        }

        var encryptionKeyHex = new string('f', 64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NOTIFICATION_ENCRYPTION_KEY"] = encryptionKeyHex
            })
            .Build();

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Test");

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            mockEnv.Object,
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        await using var runtimeCtx = await CreateRuntimeDbContextAsync(new TenantContext(tenantId));
        var bootstrapGateway = new PostgreSqlIamBootstrapGateway(runtimeCtx);
        var userLookupGateway = new PostgreSqlIamUserLookupGateway(runtimeCtx);
        var platformSessionStore = new PostgreSqlPlatformSessionStore(runtimeCtx);
        var sessionManager = new AuthSessionManager(platformSessionStore, bootstrapGateway);

        var outboxSender = new TransactionalOutboxIdentityNotificationSender(
            runtimeCtx,
            protector,
            new TenantContext(tenantId),
            NullLogger<TransactionalOutboxIdentityNotificationSender>.Instance);

        var identityService = new StaffIdentityService(
            runtimeCtx,
            bootstrapGateway,
            userLookupGateway,
            passwordHasher,
            outboxSender,
            sessionManager);

        var adminPrincipal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        // 1. Invite staff -> enqueues encrypted outbox row inside tenant transaction
        await identityService.InviteStaffAsync(
            tenantId,
            new InviteStaffCommand(email, AuthRole.Waiter, branchId.Value),
            adminPrincipal);

        // 2. Verify row exists and payload is encrypted (does NOT contain plaintext email or invite token)
        IdentityNotificationOutboxMessage? outboxMsg;
        await using (var adminCtx = CreateAdminDbContext())
        {
            outboxMsg = await adminCtx.IdentityNotificationOutbox
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.RecipientEmail == email);

            Assert.NotNull(outboxMsg);
            Assert.Equal("StaffInvitation", outboxMsg.NotificationType);
            Assert.Equal(IdentityNotificationStatus.Pending, outboxMsg.Status);
            Assert.Equal(0, outboxMsg.AttemptCount);
            Assert.DoesNotContain("token", outboxMsg.EncryptedPayload);
            Assert.DoesNotContain("invite", outboxMsg.EncryptedPayload);
        }

        // 3. Dispatch pending batch via IdentityNotificationOutboxDispatcher
        var mockTransport = new Mock<IIdentityNotificationTransport>(MockBehavior.Strict);
        string? receivedType = null;
        string? receivedEmail = null;
        string? receivedPayload = null;

        mockTransport
            .Setup(t => t.SendAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, CancellationToken>((type, recEmail, payload, idemp, ct) =>
            {
                receivedType = type;
                receivedEmail = recEmail;
                receivedPayload = payload;
            })
            .Returns(Task.CompletedTask);

        await using (var adminCtx = CreateAdminDbContext())
        {
            var dispatcher = new IdentityNotificationOutboxDispatcher(
                adminCtx,
                protector,
                mockTransport.Object,
                NullLogger<IdentityNotificationOutboxDispatcher>.Instance);

            var dispatchedCount = await dispatcher.DispatchPendingBatchAsync(batchSize: 10);
            Assert.True(dispatchedCount >= 1);
        }

        // Verify transport received decrypted payload
        Assert.Equal("StaffInvitation", receivedType);
        Assert.Equal(email, receivedEmail);
        Assert.NotNull(receivedPayload);
        Assert.Contains("invitationToken", receivedPayload);

        // 4. Verify message in database is marked Delivered
        await using (var adminCtx = CreateAdminDbContext())
        {
            var completedMsg = await adminCtx.IdentityNotificationOutbox
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == outboxMsg.Id);

            Assert.NotNull(completedMsg);
            Assert.Equal(IdentityNotificationStatus.Delivered, completedMsg.Status);
            Assert.NotNull(completedMsg.DeliveredAtUtc);
            Assert.Null(completedMsg.LastError);
        }
    }

    [Fact]
    public async Task AtomicTokenConsumption_WhenAcceptInvitationFails_TokenRemainsUnconsumed()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var email = $"atomic.{Guid.NewGuid():N}@restaurant.test";
        var passwordHasher = new AspNetCorePasswordHasher();

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("Atomic Tenant", $"at-{Guid.NewGuid():N}"[..10], tenantId);
            adminCtx.Tenants.Add(tenant);

            var brand = RestaurantOrder.Domain.Brands.Brand.Create(tenantId, "Atomic Brand", $"ab-{Guid.NewGuid():N}"[..10]);
            adminCtx.Brands.Add(brand);

            var branch = Branch.Create(tenantId, brand, "Atomic Branch", $"abr-{Guid.NewGuid():N}"[..10], "Europe/Istanbul", "TRY", branchId);
            adminCtx.Branches.Add(branch);

            await adminCtx.SaveChangesAsync();
        }

        await using var runtimeCtx = await CreateRuntimeDbContextAsync();
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

        var adminPrincipal = AuthenticatedPrincipal.CreateStaff(
            userId: Guid.NewGuid(),
            role: AuthRole.RestaurantAdmin,
            scope: AuthorizationScope.ForTenant(tenantId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Password,
            securityVersion: 1);

        await identityService.InviteStaffAsync(
            tenantId,
            new InviteStaffCommand(email, AuthRole.Waiter, branchId.Value),
            adminPrincipal);

        var token = notificationSender.LastInvitationToken;
        Assert.NotNull(token);

        // 1. Try accepting with an invalid password (too short) -> fails in validation before transaction
        await Assert.ThrowsAsync<ArgumentException>(() =>
            identityService.AcceptInvitationAsync(new AcceptInvitationCommand(token!, "short")));

        // Verify token is STILL unconsumed in database
        await using (var adminCtx = CreateAdminDbContext())
        {
            var tokenHash = ComputeSha256(token!);
            var dbToken = await adminCtx.InvitationTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
            Assert.NotNull(dbToken);
            Assert.False(dbToken.IsConsumed);
            Assert.Null(dbToken.ConsumedAtUtc);
            Assert.True(dbToken.ExpiresAtUtc > DateTimeOffset.UtcNow);
        }

        // 2. Now accept with a valid password -> succeeds and consumes token atomically
        await identityService.AcceptInvitationAsync(new AcceptInvitationCommand(token!, "ValidPassword123!"));

        // Verify token is now consumed
        await using (var adminCtx = CreateAdminDbContext())
        {
            var tokenHash = ComputeSha256(token!);
            var dbToken = await adminCtx.InvitationTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
            Assert.NotNull(dbToken);
            Assert.True(dbToken.IsConsumed);
            Assert.NotNull(dbToken.ConsumedAtUtc);
        }
    }
}
