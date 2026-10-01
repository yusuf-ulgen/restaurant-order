using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Auth;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class IdentityNotificationOutboxLeaseAndRecoveryIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public IdentityNotificationOutboxLeaseAndRecoveryIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private static string TestHexKey() => Convert.ToHexString(new byte[32]);

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
    public async Task CrashRecovery_WhenWorkerCrashesAndLeaseExpires_SecondWorkerCanReclaimAndDeliverMessage()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var tenantId = TenantId.New();
        var email = $"crash.recovery.{Guid.NewGuid():N}@restaurant.test";

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("Lease Test Tenant", $"lt-{Guid.NewGuid():N}"[..10], tenantId);
            adminCtx.Tenants.Add(tenant);
            await adminCtx.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NOTIFICATION_ENCRYPTION_KEY"] = TestHexKey()
            })
            .Build();

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Test");

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            mockEnv.Object,
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        await using var runtimeCtx = await CreateRuntimeDbContextAsync(new TenantContext(tenantId));

        var outboxSender = new TransactionalOutboxIdentityNotificationSender(
            runtimeCtx,
            protector,
            new TenantContext(tenantId),
            NullLogger<TransactionalOutboxIdentityNotificationSender>.Instance);

        await using (var tx = await runtimeCtx.BeginTenantTransactionAsync(tenantId.Value))
        {
            await outboxSender.SendPasswordResetAsync(
                email,
                "ResetToken123!",
                DateTimeOffset.UtcNow.AddHours(1));

            await runtimeCtx.SaveChangesAsync();
            await tx.CommitAsync();
        }

        var connStr = await GetRuntimeConnectionStringAsync();
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        // 1. Worker 1 claims with a very short lease (1 second)
        var worker1Token = Guid.NewGuid();
        Guid messageId;
        await using (var claimCmd = conn.CreateCommand())
        {
            claimCmd.CommandText = "SELECT id, claim_token FROM iam.claim_pending_identity_notifications(1, @now, 1, @token);";
            claimCmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            claimCmd.Parameters.AddWithValue("token", worker1Token);

            await using var reader = await claimCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            messageId = reader.GetGuid(0);
            Assert.Equal(worker1Token, reader.GetGuid(1));
        }

        // 2. Worker 2 attempts immediate claim while lease is active -> 0 messages claimed
        var worker2Token = Guid.NewGuid();
        await using (var claimCmd2 = conn.CreateCommand())
        {
            claimCmd2.CommandText = "SELECT id FROM iam.claim_pending_identity_notifications(1, @now, 60, @token);";
            claimCmd2.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            claimCmd2.Parameters.AddWithValue("token", worker2Token);

            await using var reader2 = await claimCmd2.ExecuteReaderAsync();
            Assert.False(await reader2.ReadAsync());
        }

        // 3. Worker 1 crashed; wait for lease to expire (1.5 seconds)
        await Task.Delay(1500);

        // 4. Worker 2 claims after lease expiry -> successfully re-claims the message!
        await using (var reclaimCmd = conn.CreateCommand())
        {
            reclaimCmd.CommandText = "SELECT id, claim_token FROM iam.claim_pending_identity_notifications(1, @now, 60, @token);";
            reclaimCmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            reclaimCmd.Parameters.AddWithValue("token", worker2Token);

            await using var reader3 = await reclaimCmd.ExecuteReaderAsync();
            Assert.True(await reader3.ReadAsync());
            Assert.Equal(messageId, reader3.GetGuid(0));
            Assert.Equal(worker2Token, reader3.GetGuid(1));
        }

        // 5. Worker 1 (wakes up or slow) attempts to complete with stale worker1Token -> returns false
        await using (var staleCompleteCmd = conn.CreateCommand())
        {
            staleCompleteCmd.CommandText = "SELECT iam.complete_identity_notification(@id, @claimToken, @now);";
            staleCompleteCmd.Parameters.AddWithValue("id", messageId);
            staleCompleteCmd.Parameters.AddWithValue("claimToken", worker1Token);
            staleCompleteCmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);

            var staleResult = await staleCompleteCmd.ExecuteScalarAsync();
            Assert.Equal(false, staleResult);
        }

        // 6. Worker 2 completes with valid worker2Token -> returns true
        await using (var validCompleteCmd = conn.CreateCommand())
        {
            validCompleteCmd.CommandText = "SELECT iam.complete_identity_notification(@id, @claimToken, @now);";
            validCompleteCmd.Parameters.AddWithValue("id", messageId);
            validCompleteCmd.Parameters.AddWithValue("claimToken", worker2Token);
            validCompleteCmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);

            var validResult = await validCompleteCmd.ExecuteScalarAsync();
            Assert.Equal(true, validResult);
        }

        // 7. Verify final state in DB
        await using (var adminCtx = CreateAdminDbContext())
        {
            var msg = await adminCtx.IdentityNotificationOutbox
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == messageId);

            Assert.NotNull(msg);
            Assert.Equal(IdentityNotificationStatus.Delivered, msg.Status);
            Assert.Null(msg.ClaimToken);
            Assert.Null(msg.LockedUntilUtc);
            Assert.NotNull(msg.DeliveredAtUtc);
        }
    }

    [Fact]
    public async Task StaleFailure_WhenWorkerFailsAfterLeaseExpiryAndReclaim_DoesNotOverwriteState()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var tenantId = TenantId.New();
        var email = $"stale.fail.{Guid.NewGuid():N}@restaurant.test";

        await using (var adminCtx = CreateAdminDbContext())
        {
            var tenant = Tenant.Create("Stale Fail Tenant", $"sft-{Guid.NewGuid():N}"[..10], tenantId);
            adminCtx.Tenants.Add(tenant);
            await adminCtx.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NOTIFICATION_ENCRYPTION_KEY"] = TestHexKey()
            })
            .Build();

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Test");

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            mockEnv.Object,
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        await using var runtimeCtx = await CreateRuntimeDbContextAsync(new TenantContext(tenantId));

        var outboxSender = new TransactionalOutboxIdentityNotificationSender(
            runtimeCtx,
            protector,
            new TenantContext(tenantId),
            NullLogger<TransactionalOutboxIdentityNotificationSender>.Instance);

        await using (var tx = await runtimeCtx.BeginTenantTransactionAsync(tenantId.Value))
        {
            await outboxSender.SendInvitationAsync(
                email,
                "InviteToken123!",
                DateTimeOffset.UtcNow.AddDays(1));

            await runtimeCtx.SaveChangesAsync();
            await tx.CommitAsync();
        }

        var connStr = await GetRuntimeConnectionStringAsync();
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        // 1. Worker 1 claims with 1 second lease
        var worker1Token = Guid.NewGuid();
        Guid messageId;
        await using (var claimCmd = conn.CreateCommand())
        {
            claimCmd.CommandText = "SELECT id, claim_token FROM iam.claim_pending_identity_notifications(1, @now, 1, @token);";
            claimCmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            claimCmd.Parameters.AddWithValue("token", worker1Token);

            await using var reader = await claimCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            messageId = reader.GetGuid(0);
        }

        await Task.Delay(1500);

        // 2. Worker 2 reclaims
        var worker2Token = Guid.NewGuid();
        await using (var reclaimCmd = conn.CreateCommand())
        {
            reclaimCmd.CommandText = "SELECT id, claim_token FROM iam.claim_pending_identity_notifications(1, @now, 60, @token);";
            reclaimCmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            reclaimCmd.Parameters.AddWithValue("token", worker2Token);

            await using var reader = await reclaimCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
        }

        // 3. Worker 1 attempts to record failure with old token -> returns false
        await using (var staleFailCmd = conn.CreateCommand())
        {
            staleFailCmd.CommandText = "SELECT iam.fail_identity_notification(@id, @claimToken, @error, @nextAttempt, @isDeadLetter, @now);";
            staleFailCmd.Parameters.AddWithValue("id", messageId);
            staleFailCmd.Parameters.AddWithValue("claimToken", worker1Token);
            staleFailCmd.Parameters.AddWithValue("error", "Stale worker error");
            staleFailCmd.Parameters.AddWithValue("nextAttempt", DateTimeOffset.UtcNow.AddMinutes(5));
            staleFailCmd.Parameters.AddWithValue("isDeadLetter", false);
            staleFailCmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);

            var failResult = await staleFailCmd.ExecuteScalarAsync();
            Assert.Equal(false, failResult);
        }

        // 4. Message should still be in Processing state with Worker 2's token
        await using (var adminCtx = CreateAdminDbContext())
        {
            var msg = await adminCtx.IdentityNotificationOutbox
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == messageId);

            Assert.NotNull(msg);
            Assert.Equal(IdentityNotificationStatus.Processing, msg.Status);
            Assert.Equal(worker2Token, msg.ClaimToken);
        }
    }
}
