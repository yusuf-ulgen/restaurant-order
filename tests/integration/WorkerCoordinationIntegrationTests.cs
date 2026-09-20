using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Worker.Safety;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Real Redis integration tests using Testcontainers for distributed worker lease management,
/// concurrency/race conditions, active slot transitions, and idempotency guarantees.
/// </summary>
public class WorkerCoordinationIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public WorkerCoordinationIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Concurrency_TwoWorkersRequestLeaseSimultaneously_OnlyOneAcquires()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var logger = NullLogger<RedisWorkerLeaseManager>.Instance;

        var leaseKey = $"test-lease-concurrency:{Guid.NewGuid():N}";
        var workerA = new RedisWorkerLeaseManager(redis, logger);
        var workerB = new RedisWorkerLeaseManager(redis, logger);

        // Run both acquisition attempts concurrently
        var taskA = workerA.TryAcquireLeaseAsync(leaseKey, TimeSpan.FromSeconds(30));
        var taskB = workerB.TryAcquireLeaseAsync(leaseKey, TimeSpan.FromSeconds(30));

        var results = await Task.WhenAll(taskA, taskB);

        // Exactly one worker must acquire the lease
        Assert.Single(results, r => r);
        Assert.Single(results, r => !r);

        // Cleanup
        var holder = results[0] ? workerA : workerB;
        await holder.ReleaseLeaseAsync(leaseKey);
    }

    [Fact]
    public async Task Lease_WrongTokenCannotRenewOrRelease()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var logger = NullLogger<RedisWorkerLeaseManager>.Instance;

        var leaseKey = $"test-lease-token:{Guid.NewGuid():N}";
        var workerA = new RedisWorkerLeaseManager(redis, logger);
        var workerB = new RedisWorkerLeaseManager(redis, logger);

        // Worker A acquires the lease
        var acquired = await workerA.TryAcquireLeaseAsync(leaseKey, TimeSpan.FromSeconds(30));
        Assert.True(acquired);

        // Worker B attempts to renew Worker A's lease -> Rejected
        var renewedByB = await workerB.RenewLeaseAsync(leaseKey, TimeSpan.FromSeconds(30));
        Assert.False(renewedByB);

        // Worker B attempts to release Worker A's lease -> Rejected
        await workerB.ReleaseLeaseAsync(leaseKey);

        // Verify Worker A still owns the lease and can renew it
        var renewedByA = await workerA.RenewLeaseAsync(leaseKey, TimeSpan.FromSeconds(30));
        Assert.True(renewedByA);

        // Worker A releases the lease
        await workerA.ReleaseLeaseAsync(leaseKey);

        // Now Worker B can acquire it
        var acquiredByB = await workerB.TryAcquireLeaseAsync(leaseKey, TimeSpan.FromSeconds(30));
        Assert.True(acquiredByB);

        await workerB.ReleaseLeaseAsync(leaseKey);
    }

    [Fact]
    public async Task Lease_AfterTtlExpiry_AnotherWorkerCanAcquire()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var logger = NullLogger<RedisWorkerLeaseManager>.Instance;

        var leaseKey = $"test-lease-expiry:{Guid.NewGuid():N}";
        var workerA = new RedisWorkerLeaseManager(redis, logger);
        var workerB = new RedisWorkerLeaseManager(redis, logger);

        // Worker A acquires with a short 1-second TTL
        var acquiredA = await workerA.TryAcquireLeaseAsync(leaseKey, TimeSpan.FromSeconds(1));
        Assert.True(acquiredA);

        // Worker B cannot acquire immediately
        var acquiredBInitial = await workerB.TryAcquireLeaseAsync(leaseKey, TimeSpan.FromSeconds(5));
        Assert.False(acquiredBInitial);

        // Wait for TTL to expire
        await Task.Delay(1200);

        // Now Worker B acquires successfully
        var acquiredBAfterExpiry = await workerB.TryAcquireLeaseAsync(leaseKey, TimeSpan.FromSeconds(5));
        Assert.True(acquiredBAfterExpiry);

        await workerB.ReleaseLeaseAsync(leaseKey);
    }

    [Fact]
    public async Task CentralActiveSlot_TransitionsBetweenSlotsCorrectly()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var db = redis.GetDatabase();
        var logger = NullLogger<RedisWorkerActivationGuard>.Instance;

        var blueGuard = new RedisWorkerActivationGuard(redis, logger, slotColor: "blue");
        var greenGuard = new RedisWorkerActivationGuard(redis, logger, slotColor: "green");

        // 1. Set active slot to "green" in Redis
        await db.StringSetAsync(RedisWorkerActivationGuard.DefaultActiveSlotKey, "green");

        var isBlueActive = await blueGuard.IsActiveSlotAsync();
        var isGreenActive = await greenGuard.IsActiveSlotAsync();

        Assert.False(isBlueActive);
        Assert.True(isGreenActive);

        // 2. Simulate cutover / rollback: switch active slot to "blue"
        await db.StringSetAsync(RedisWorkerActivationGuard.DefaultActiveSlotKey, "blue");

        var isBlueActiveAfter = await blueGuard.IsActiveSlotAsync();
        var isGreenActiveAfter = await greenGuard.IsActiveSlotAsync();

        Assert.True(isBlueActiveAfter);
        Assert.False(isGreenActiveAfter);
    }

    [Fact]
    public async Task IdempotencyStore_DeduplicatesConcurrentReservations()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var store = new RedisIdempotencyStore(redis, NullLogger<RedisIdempotencyStore>.Instance);

        var idempotencyKey = $"order-print:{Guid.NewGuid():N}";

        // First attempt succeeds
        var first = await store.TryReserveKeyAsync(idempotencyKey, TimeSpan.FromMinutes(5));
        Assert.True(first);

        // Second concurrent or duplicate attempt is rejected
        var second = await store.TryReserveKeyAsync(idempotencyKey, TimeSpan.FromMinutes(5));
        Assert.False(second);

        // Mark completed
        await store.MarkCompletedAsync(idempotencyKey, TimeSpan.FromMinutes(5));
        var isDone = await store.IsCompletedAsync(idempotencyKey);
        Assert.True(isDone);
    }
}
