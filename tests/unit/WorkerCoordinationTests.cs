using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Worker;
using RestaurantOrder.Worker.Safety;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class WorkerCoordinationTests
{
    private class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "RestaurantOrder.Worker";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private class FakeWorkerActivationGuard : IWorkerActivationGuard
    {
        public string SlotColor { get; set; } = "green";
        public string ActiveSlot { get; set; } = "green";
        public WorkerActivationStatus Status { get; set; } = WorkerActivationStatus.Active;
        public bool IsActive { get; set; } = true;

        public Task<bool> IsActiveSlotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IsActive);
        }
    }

    private class FakeWorkerLeaseManager : IWorkerLeaseManager
    {
        public string OwnerToken { get; set; } = "test-token-123";
        public bool CanAcquire { get; set; } = true;
        public bool CanRenew { get; set; } = true;
        public bool ReleaseCalled { get; set; }
        public int AcquireCalls { get; set; }
        public int RenewCalls { get; set; }

        public Task<bool> TryAcquireLeaseAsync(string leaseKey, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            AcquireCalls++;
            return Task.FromResult(CanAcquire);
        }

        public Task<bool> RenewLeaseAsync(string leaseKey, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            RenewCalls++;
            return Task.FromResult(CanRenew);
        }

        public Task ReleaseLeaseAsync(string leaseKey, CancellationToken cancellationToken = default)
        {
            ReleaseCalled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Worker_WhenSlotIsActiveAndLeaseAcquired_BecomesLeader()
    {
        var guard = new FakeWorkerActivationGuard { IsActive = true, SlotColor = "green" };
        var lease = new FakeWorkerLeaseManager { CanAcquire = true };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "WORKER_LEASE_TTL_SECONDS", "1" },
                { "WORKER_LEASE_RENEW_INTERVAL_SECONDS", "1" },
                { "WORKER_STANDBY_POLL_INTERVAL_SECONDS", "1" },
            })
            .Build();

        var worker = new Worker.Worker(guard, lease, config, NullLogger<Worker.Worker>.Instance);

        using var cts = new CancellationTokenSource();
        var execTask = worker.StartAsync(cts.Token);

        // Allow worker loop to execute initial acquisition
        await Task.Delay(100);
        Assert.True(worker.IsLeader);
        Assert.True(lease.AcquireCalls >= 1);

        await worker.StopAsync(CancellationToken.None);
        Assert.True(lease.ReleaseCalled);
        Assert.False(worker.IsLeader);
    }

    [Fact]
    public async Task Worker_WhenSlotIsInactive_DoesNotAcquireLeaseAndRemainsStandby()
    {
        var guard = new FakeWorkerActivationGuard { IsActive = false, SlotColor = "blue" };
        var lease = new FakeWorkerLeaseManager { CanAcquire = true };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "WORKER_STANDBY_POLL_INTERVAL_SECONDS", "1" }
            })
            .Build();

        var worker = new Worker.Worker(guard, lease, config, NullLogger<Worker.Worker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        await Task.Delay(100);
        Assert.False(worker.IsLeader);
        Assert.Equal(0, lease.AcquireCalls);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Worker_WhenActiveSlotChangesToInactive_ImmediatelyReleasesLease()
    {
        var guard = new FakeWorkerActivationGuard { IsActive = true, SlotColor = "green" };
        var lease = new FakeWorkerLeaseManager { CanAcquire = true };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "WORKER_LEASE_RENEW_INTERVAL_SECONDS", "1" },
                { "WORKER_STANDBY_POLL_INTERVAL_SECONDS", "1" }
            })
            .Build();

        var worker = new Worker.Worker(guard, lease, config, NullLogger<Worker.Worker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        await Task.Delay(100);
        Assert.True(worker.IsLeader);

        // Simulate cutover: active slot changes, guard becomes inactive
        guard.IsActive = false;
        await Task.Delay(1100);

        Assert.False(worker.IsLeader);
        Assert.True(lease.ReleaseCalled);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Worker_WhenLeaseRenewalFails_ImmediatelyHaltsLeadership()
    {
        var guard = new FakeWorkerActivationGuard { IsActive = true, SlotColor = "green" };
        var lease = new FakeWorkerLeaseManager { CanAcquire = true, CanRenew = true };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "WORKER_LEASE_RENEW_INTERVAL_SECONDS", "1" },
                { "WORKER_STANDBY_POLL_INTERVAL_SECONDS", "1" }
            })
            .Build();

        var worker = new Worker.Worker(guard, lease, config, NullLogger<Worker.Worker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        await Task.Delay(100);
        Assert.True(worker.IsLeader);

        // Next renewal fails (e.g. Redis disconnection or TTL expired)
        lease.CanRenew = false;
        await Task.Delay(1200);

        Assert.False(worker.IsLeader);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RedisWorkerLeaseManager_WhenConfigMissingInProduction_FailsClosed()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var logger = NullLogger<RedisWorkerLeaseManager>.Instance;

        using var manager = new RedisWorkerLeaseManager(config, env, logger);

        Assert.NotEmpty(manager.OwnerToken);
        var acquired = await manager.TryAcquireLeaseAsync("test-key", TimeSpan.FromSeconds(10));
        var renewed = await manager.RenewLeaseAsync("test-key", TimeSpan.FromSeconds(10));

        Assert.False(acquired);
        Assert.False(renewed);
    }

    [Fact]
    public async Task RedisWorkerActivationGuard_WhenConfigMissingInProduction_FailsClosed()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var logger = NullLogger<RedisWorkerActivationGuard>.Instance;

        using var guard = new RedisWorkerActivationGuard(config, env, logger);

        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal(WorkerActivationStatus.Error, guard.Status);
    }

    [Fact]
    public async Task RedisIdempotencyStore_WhenConfigMissing_FailsClosedAndRejectsNullKey()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var logger = NullLogger<RedisIdempotencyStore>.Instance;

        using var store = new RedisIdempotencyStore(config, env, logger);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.TryReserveKeyAsync("", TimeSpan.FromMinutes(5)));

        var reserved = await store.TryReserveKeyAsync("test-job-key", TimeSpan.FromMinutes(5));
        Assert.False(reserved);
    }
}
