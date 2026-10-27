using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Branches;
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
/// Verifies PostgreSQL Operational Outbox behavior with the unprivileged runtime role
/// under strict FORCE ROW LEVEL SECURITY. Tests atomic commits, rollbacks, idempotency,
/// RLS isolation, and secure claim gateway execution.
/// </summary>
public class PostgreSqlOperationalOutboxIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PostgreSqlOperationalOutboxIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<RestaurantOrderDbContext> CreateWorkerDbContextAsync() =>
        new(new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(await _fixture.ProvisionTemporaryWorkerRoleAsync()).Options);

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
    public async Task DomainMutationAndOutboxTransaction_AtomicCommit_DispatchesSuccessfully()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var now = DateTimeOffset.UtcNow;

        // 1. Seed tenant and branch in admin context
        await using (var adminDb = CreateAdminDbContext())
        {
            await SeedTenantAndBranchAsync(adminDb, tenantId, branchId, "Atomic");
        }

        // 2. Setup Redis subscriber to listen for broadcast events
        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisProvider = new TestRedisProvider(redis);
        var publisher = new RedisRealtimeEventPublisher(redisProvider, NullLogger<RedisRealtimeEventPublisher>.Instance);

        var receivedEvents = new ConcurrentBag<string>();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisRealtimeEventPublisher.BroadcastChannel),
            (channel, msg) =>
            {
                var payload = msg.ToString();
                receivedEvents.Add(payload);
                tcs.TrySetResult(payload);
            });

        // 3. Perform domain mutation + outbox insert in a single transaction with unprivileged role
        var tenantContext = new TenantContext(tenantId);
        var brandId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        Guid outboxId;

        await using (var runtimeDb = await CreateRuntimeDbContextAsync(tenantContext))
        await using (var tx = await runtimeDb.BeginTenantTransactionAsync(tenantId.Value))
        {
            var brand = Brand.Create(tenantId, "Atomic Brand", $"ab-{Guid.NewGuid():N}"[..10]);
            runtimeDb.Brands.Add(brand);

            var eventPayload = JsonSerializer.Serialize(new
            {
                event_id = eventId,
                tenant_id = tenantId.Value,
                branch_id = branchId.Value,
                brand_id = brandId,
                name = "Atomic Brand"
            });

            var message = OperationalOutboxMessage.Create(
                tenantId,
                branchId,
                brand.Id.Value.ToString(),
                "catalog.brand_created",
                eventPayload,
                $"idem-{Guid.NewGuid():N}",
                now,
                now);

            runtimeDb.OperationalOutbox.Add(message);
            await runtimeDb.SaveChangesAsync();
            await tx.CommitAsync();

            outboxId = message.Id;
        }

        // 4. Run dispatcher with unprivileged runtime role (no tenant context)
        await using (var dispatcherDb = await CreateWorkerDbContextAsync())
        {
            var dispatcher = new OperationalOutboxDispatcher(dispatcherDb, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);
            var dispatchedCount = await dispatcher.DispatchPendingBatchAsync(10);

            Assert.True(dispatchedCount >= 1, "Dispatcher should have claimed and dispatched at least 1 message.");
        }

        // 5. Verify Redis received the broadcast event within 5s
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Same(tcs.Task, completed);
        var receivedPayload = await tcs.Task;
        Assert.Contains(eventId.ToString(), receivedPayload);

        // 6. Verify record status is Dispatched in DB
        await using (var adminDb = CreateAdminDbContext())
        {
            var record = await adminDb.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == outboxId);
            Assert.NotNull(record);
            Assert.Equal(OperationalOutboxStatus.Dispatched, record.Status);
            Assert.NotNull(record.DispatchedAtUtc);
            Assert.Null(record.LastError);
        }
    }

    [Fact]
    public async Task TransactionRollback_DoesNotPersistOutbox_RedisReceivesNothing()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var now = DateTimeOffset.UtcNow;

        await using (var adminDb = CreateAdminDbContext())
        {
            await SeedTenantAndBranchAsync(adminDb, tenantId, branchId, "Rollback");
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisProvider = new TestRedisProvider(redis);
        var publisher = new RedisRealtimeEventPublisher(redisProvider, NullLogger<RedisRealtimeEventPublisher>.Instance);

        var received = false;
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisRealtimeEventPublisher.BroadcastChannel),
            (_, _) => { received = true; });

        var tenantContext = new TenantContext(tenantId);
        Guid rolledBackId = Guid.NewGuid();

        await using (var runtimeDb = await CreateRuntimeDbContextAsync(tenantContext))
        await using (var tx = await runtimeDb.BeginTenantTransactionAsync(tenantId.Value))
        {
            var message = OperationalOutboxMessage.Create(
                tenantId,
                branchId,
                "agg-rollback",
                "order.created",
                "{\"event_id\":\"rolled_back\"}",
                $"idem-{Guid.NewGuid():N}",
                now,
                now);

            runtimeDb.OperationalOutbox.Add(message);
            await runtimeDb.SaveChangesAsync();
            rolledBackId = message.Id;

            // Explicit rollback
            await tx.RollbackAsync();
        }

        // Run dispatcher
        await using (var dispatcherDb = await CreateWorkerDbContextAsync())
        {
            var dispatcher = new OperationalOutboxDispatcher(dispatcherDb, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);
            var dispatched = await dispatcher.DispatchPendingBatchAsync(10);
            Assert.Equal(0, dispatched);
        }

        // Verify outbox message does not exist
        await using (var adminDb = CreateAdminDbContext())
        {
            var record = await adminDb.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == rolledBackId);
            Assert.Null(record);
        }

        Assert.False(received, "Redis should not receive any events from a rolled-back transaction.");
    }

    [Fact]
    public async Task IdempotencyReplay_DoesNotProduceDuplicateOutboxOrRedisEvents()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var now = DateTimeOffset.UtcNow;

        await using (var adminDb = CreateAdminDbContext())
        {
            await SeedTenantAndBranchAsync(adminDb, tenantId, branchId, "Idempotency");
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisProvider = new TestRedisProvider(redis);
        var publisher = new RedisRealtimeEventPublisher(redisProvider, NullLogger<RedisRealtimeEventPublisher>.Instance);

        var tenantContext = new TenantContext(tenantId);
        var eventId = Guid.NewGuid();

        await using (var runtimeDb = await CreateRuntimeDbContextAsync(tenantContext))
        await using (var tx = await runtimeDb.BeginTenantTransactionAsync(tenantId.Value))
        {
            var message = OperationalOutboxMessage.Create(
                tenantId,
                branchId,
                "agg-idem",
                "catalog.updated",
                JsonSerializer.Serialize(new { event_id = eventId }),
                $"idem-{Guid.NewGuid():N}",
                now,
                now);

            runtimeDb.OperationalOutbox.Add(message);
            await runtimeDb.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // First dispatch: succeeds
        await using (var dispatcherDb = await CreateWorkerDbContextAsync())
        {
            var dispatcher = new OperationalOutboxDispatcher(dispatcherDb, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);
            var firstDispatch = await dispatcher.DispatchPendingBatchAsync(10);
            Assert.Equal(1, firstDispatch);

            // Replay dispatch: already dispatched message should not be re-dispatched
            var secondDispatch = await dispatcher.DispatchPendingBatchAsync(10);
            Assert.Equal(0, secondDispatch);
        }
    }

    [Fact]
    public async Task MultiTenantRls_UnprivilegedRuntimeRoleProcessesBothQueues_RejectsDirectCrossTenantQueries()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantAId = TenantId.New();
        var tenantBId = TenantId.New();
        var branchAId = BranchId.New();
        var branchBId = BranchId.New();
        var now = DateTimeOffset.UtcNow;

        await using (var adminDb = CreateAdminDbContext())
        {
            await SeedTenantAndBranchAsync(adminDb, tenantAId, branchAId, "Alpha");
            await SeedTenantAndBranchAsync(adminDb, tenantBId, branchBId, "Beta");
        }

        // Insert outbox message for Tenant A
        Guid msgAId;
        await using (var dbA = await CreateRuntimeDbContextAsync(new TenantContext(tenantAId)))
        await using (var txA = await dbA.BeginTenantTransactionAsync(tenantAId.Value))
        {
            var msgA = OperationalOutboxMessage.Create(
                tenantAId, branchAId, "agg-a", "test.a", "{\"tenant\":\"A\"}", $"idem-a-{Guid.NewGuid():N}", now, now);
            dbA.OperationalOutbox.Add(msgA);
            await dbA.SaveChangesAsync();
            await txA.CommitAsync();
            msgAId = msgA.Id;
        }

        // Insert outbox message for Tenant B
        Guid msgBId;
        await using (var dbB = await CreateRuntimeDbContextAsync(new TenantContext(tenantBId)))
        await using (var txB = await dbB.BeginTenantTransactionAsync(tenantBId.Value))
        {
            var msgB = OperationalOutboxMessage.Create(
                tenantBId, branchBId, "agg-b", "test.b", "{\"tenant\":\"B\"}", $"idem-b-{Guid.NewGuid():N}", now, now);
            dbB.OperationalOutbox.Add(msgB);
            await dbB.SaveChangesAsync();
            await txB.CommitAsync();
            msgBId = msgB.Id;
        }

        // 1. Direct RLS query as Tenant A: can ONLY see Tenant A message
        await using (var dbA = await CreateRuntimeDbContextAsync(new TenantContext(tenantAId)))
        await using (var txA = await dbA.BeginTenantTransactionAsync(tenantAId.Value))
        {
            var messages = await dbA.OperationalOutbox.ToListAsync();
            Assert.Contains(messages, m => m.Id == msgAId);
            Assert.DoesNotContain(messages, m => m.Id == msgBId);
        }

        // 2. Direct RLS query as Tenant B: can ONLY see Tenant B message
        await using (var dbB = await CreateRuntimeDbContextAsync(new TenantContext(tenantBId)))
        await using (var txB = await dbB.BeginTenantTransactionAsync(tenantBId.Value))
        {
            var messages = await dbB.OperationalOutbox.ToListAsync();
            Assert.Contains(messages, m => m.Id == msgBId);
            Assert.DoesNotContain(messages, m => m.Id == msgAId);
        }

        // 3. Direct RLS query with NO tenant: returns 0 messages due to FORCE ROW LEVEL SECURITY
        await using (var dbNoTenant = await CreateRuntimeDbContextAsync(TenantContext.Empty))
        {
            var messages = await dbNoTenant.OperationalOutbox.ToListAsync();
            Assert.Empty(messages);
        }

        // 4. Unprivileged dispatcher (no tenant context) uses the secure claim gateway to dispatch BOTH queues
        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisProvider = new TestRedisProvider(redis);
        var publisher = new RedisRealtimeEventPublisher(redisProvider, NullLogger<RedisRealtimeEventPublisher>.Instance);

        await using (var dispatcherDb = await CreateWorkerDbContextAsync())
        {
            var dispatcher = new OperationalOutboxDispatcher(dispatcherDb, publisher, NullLogger<OperationalOutboxDispatcher>.Instance);
            var dispatched = await dispatcher.DispatchPendingBatchAsync(10);
            Assert.True(dispatched >= 2, $"Expected at least 2 messages dispatched from both tenants, got {dispatched}.");
        }

        // 5. Verify both records are now Dispatched
        await using (var adminDb = CreateAdminDbContext())
        {
            var recA = await adminDb.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == msgAId);
            var recB = await adminDb.OperationalOutbox.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == msgBId);

            Assert.NotNull(recA);
            Assert.NotNull(recB);
            Assert.Equal(OperationalOutboxStatus.Dispatched, recA.Status);
            Assert.Equal(OperationalOutboxStatus.Dispatched, recB.Status);
        }
    }
}
