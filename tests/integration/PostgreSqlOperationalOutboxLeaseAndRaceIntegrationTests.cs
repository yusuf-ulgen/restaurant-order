using System;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Events;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Verifies claim ownership, concurrency, worker lease expiration, leadership loss,
/// and poison payload protection on real PostgreSQL and Redis instances.
/// </summary>
public class PostgreSqlOperationalOutboxLeaseAndRaceIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PostgreSqlOperationalOutboxLeaseAndRaceIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class TestRedisProvider : IRedisDatabaseProvider
    {
        private readonly ConnectionMultiplexer _multiplexer;

        public TestRedisProvider(ConnectionMultiplexer multiplexer)
        {
            _multiplexer = multiplexer;
        }

        public Task<IDatabase?> GetDatabaseAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDatabase?>(_multiplexer.GetDatabase());

        public Task<IDatabase> GetRequiredDatabaseAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_multiplexer.GetDatabase());

        public Task<ISubscriber?> GetSubscriberAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ISubscriber?>(_multiplexer.GetSubscriber());

        public Task<ISubscriber> GetRequiredSubscriberAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_multiplexer.GetSubscriber());
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

    private async Task<string> GetRuntimeConnectionStringAsync()
    {
        await EnsureMigrationsAppliedAsync();
        return await _fixture.ProvisionTemporaryRuntimeRoleAsync();
    }

    private async Task<RestaurantOrderDbContext> CreateRuntimeDbContextAsync(ITenantContext? tenantContext = null)
    {
        var connStr = await GetRuntimeConnectionStringAsync();
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new RestaurantOrderDbContext(options, tenantContext ?? TenantContext.Empty);
    }

    private RestaurantOrderDbContext CreateAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;

        return new RestaurantOrderDbContext(options, TenantContext.Empty);
    }

    private static async Task SeedTenantAndBranchAsync(
        RestaurantOrderDbContext adminDb,
        TenantId tenantId,
        BranchId branchId,
        string prefix)
    {
        var tenant = Tenant.Create($"{prefix} Tenant", $"t-{Guid.NewGuid():N}"[..10], tenantId);
        var brand = Brand.Create(tenantId, $"{prefix} Brand", $"b-{Guid.NewGuid():N}"[..10]);
        var branch = Branch.Create(tenantId, brand.Id, $"{prefix} Branch", $"br-{Guid.NewGuid():N}"[..10], id: branchId);
        adminDb.Tenants.Add(tenant);
        adminDb.Brands.Add(brand);
        adminDb.Branches.Add(branch);
        await adminDb.SaveChangesAsync();
    }

    [Fact]
    public async Task StaleClaimAndWorkerRace_WhenLeaseExpires_SecondWorkerReclaims_OldWorkerCannotComplete()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var now = DateTimeOffset.UtcNow;

        await using (var adminDb = CreateAdminDbContext())
        {
            await SeedTenantAndBranchAsync(adminDb, tenantId, branchId, "Race");
        }

        // Insert pending outbox record
        Guid outboxId;
        await using (var runtimeDb = await CreateRuntimeDbContextAsync(new TenantContext(tenantId)))
        await using (var tx = await runtimeDb.BeginTenantTransactionAsync(tenantId.Value))
        {
            var message = OperationalOutboxMessage.Create(
                tenantId,
                branchId,
                "agg-race",
                "test.race",
                "{\"key\":\"value\"}",
                $"idem-race-{Guid.NewGuid():N}",
                now,
                now);

            runtimeDb.OperationalOutbox.Add(message);
            await runtimeDb.SaveChangesAsync();
            await tx.CommitAsync();
            outboxId = message.Id;
        }

        var runtimeConnStr = await GetRuntimeConnectionStringAsync();
        var worker1ClaimId = $"worker-1-{Guid.NewGuid():N}";
        var worker2ClaimId = $"worker-2-{Guid.NewGuid():N}";

        // 1. Worker 1 claims with lease = 1 second
        bool worker1Claimed = false;
        await using (var conn1 = new NpgsqlConnection(runtimeConnStr))
        {
            await conn1.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT id, lock_claim_id FROM tenancy.claim_pending_operational_outbox(10, NOW(), 1, @claimId, NULL);", conn1);
            cmd.Parameters.AddWithValue("claimId", worker1ClaimId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetGuid(0) == outboxId)
                {
                    worker1Claimed = true;
                    Assert.Equal(worker1ClaimId, reader.GetString(1));
                }
            }
        }

        Assert.True(worker1Claimed, "Worker 1 should have claimed the pending record.");

        // 2. Wait 1.5 seconds for Worker 1 lease to expire
        await Task.Delay(1500);

        // 3. Worker 2 claims the expired record with lease = 60 seconds
        bool worker2Claimed = false;
        await using (var conn2 = new NpgsqlConnection(runtimeConnStr))
        {
            await conn2.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT id, lock_claim_id FROM tenancy.claim_pending_operational_outbox(10, NOW(), 60, @claimId, NULL);", conn2);
            cmd.Parameters.AddWithValue("claimId", worker2ClaimId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetGuid(0) == outboxId)
                {
                    worker2Claimed = true;
                    Assert.Equal(worker2ClaimId, reader.GetString(1));
                }
            }
        }

        Assert.True(worker2Claimed, "Worker 2 should have reclaimed the expired record.");

        // 4. Stale Worker 1 wakes up and attempts to complete with its old claim ID
        bool worker1CompletionSuccess;
        await using (var conn1 = new NpgsqlConnection(runtimeConnStr))
        {
            await conn1.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT tenancy.complete_operational_outbox(@id, @claimId, NOW());", conn1);
            cmd.Parameters.AddWithValue("id", outboxId);
            cmd.Parameters.AddWithValue("claimId", worker1ClaimId);
            worker1CompletionSuccess = (bool)(await cmd.ExecuteScalarAsync() ?? false);
        }

        // Stale completion MUST fail
        Assert.False(worker1CompletionSuccess, "Stale worker completion must be rejected by claim gateway.");

        // 5. Worker 2 completes with its valid claim ID
        bool worker2CompletionSuccess;
        await using (var conn2 = new NpgsqlConnection(runtimeConnStr))
        {
            await conn2.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT tenancy.complete_operational_outbox(@id, @claimId, NOW());", conn2);
            cmd.Parameters.AddWithValue("id", outboxId);
            cmd.Parameters.AddWithValue("claimId", worker2ClaimId);
            worker2CompletionSuccess = (bool)(await cmd.ExecuteScalarAsync() ?? false);
        }

        // Worker 2 completion MUST succeed
        Assert.True(worker2CompletionSuccess, "Active worker completion must succeed.");

        // 6. Verify final state in DB
        await using (var adminDb = CreateAdminDbContext())
        {
            var record = await adminDb.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == outboxId);
            Assert.NotNull(record);
            Assert.Equal(OperationalOutboxStatus.Dispatched, record.Status);
            Assert.Null(record.LockClaimId);
            Assert.NotNull(record.DispatchedAtUtc);
        }
    }

    [Fact]
    public async Task LeadershipLeaseLoss_DispatcherAbortsBatchPublication()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var now = DateTimeOffset.UtcNow;

        await using (var adminDb = CreateAdminDbContext())
        {
            await SeedTenantAndBranchAsync(adminDb, tenantId, branchId, "Leadership");
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisProvider = new TestRedisProvider(redis);
        var publisher = new RedisRealtimeEventPublisher(redisProvider, NullLogger<RedisRealtimeEventPublisher>.Instance);

        var receivedCount = 0;
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisRealtimeEventPublisher.BroadcastChannel),
            (_, _) => { Interlocked.Increment(ref receivedCount); });

        // Insert pending outbox record
        await using (var runtimeDb = await CreateRuntimeDbContextAsync(new TenantContext(tenantId)))
        await using (var tx = await runtimeDb.BeginTenantTransactionAsync(tenantId.Value))
        {
            var message = OperationalOutboxMessage.Create(
                tenantId,
                branchId,
                "agg-lead",
                "test.leadership",
                "{\"key\":\"value\"}",
                $"idem-lead-{Guid.NewGuid():N}",
                now,
                now);

            runtimeDb.OperationalOutbox.Add(message);
            await runtimeDb.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // Run dispatcher with isLeaseValid returning false (lease lost mid-batch)
        await using (var dispatcherDb = await CreateRuntimeDbContextAsync(TenantContext.Empty))
        {
            var dispatcher = new OperationalOutboxDispatcher(dispatcherDb, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);
            var dispatched = await dispatcher.DispatchPendingBatchAsync(10, isLeaseValid: _ => Task.FromResult(false));

            Assert.Equal(0, dispatched);
        }

        Assert.Equal(0, receivedCount);
    }

    [Fact]
    public async Task PoisonPayload_IsDeadLetteredDirectlyWithoutPublishingToRedis()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var now = DateTimeOffset.UtcNow;

        await using (var adminDb = CreateAdminDbContext())
        {
            await SeedTenantAndBranchAsync(adminDb, tenantId, branchId, "Poison");
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisProvider = new TestRedisProvider(redis);
        var publisher = new RedisRealtimeEventPublisher(redisProvider, NullLogger<RedisRealtimeEventPublisher>.Instance);

        var received = false;
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisRealtimeEventPublisher.BroadcastChannel),
            (_, _) => { received = true; });

        // Insert outbox message with invalid JSON poison payload
        Guid poisonId;
        await using (var runtimeDb = await CreateRuntimeDbContextAsync(new TenantContext(tenantId)))
        await using (var tx = await runtimeDb.BeginTenantTransactionAsync(tenantId.Value))
        {
            var message = OperationalOutboxMessage.Create(
                tenantId,
                branchId,
                "agg-poison",
                "test.poison",
                "[1, 2, 3, \"not an object\"]",
                $"idem-poison-{Guid.NewGuid():N}",
                now,
                now);

            runtimeDb.OperationalOutbox.Add(message);
            await runtimeDb.SaveChangesAsync();
            await tx.CommitAsync();
            poisonId = message.Id;
        }

        // Run dispatcher
        await using (var dispatcherDb = await CreateRuntimeDbContextAsync(TenantContext.Empty))
        {
            var dispatcher = new OperationalOutboxDispatcher(dispatcherDb, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);
            var dispatched = await dispatcher.DispatchPendingBatchAsync(10);

            // Poison payload must not count as dispatched
            Assert.Equal(0, dispatched);
        }

        Assert.False(received, "Poison payload must never be published to Redis.");

        // Verify status in DB is DeadLetter
        await using (var adminDb = CreateAdminDbContext())
        {
            var record = await adminDb.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == poisonId);
            Assert.NotNull(record);
            Assert.Equal(OperationalOutboxStatus.DeadLetter, record.Status);
            Assert.NotNull(record.LastError);
            Assert.Contains("Poison", record.LastError);
        }
    }
}
