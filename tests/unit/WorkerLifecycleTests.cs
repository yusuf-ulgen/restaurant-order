using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Worker.Safety;
using Xunit;
using WorkerService = RestaurantOrder.Worker.Worker;

namespace RestaurantOrder.UnitTests;

public class WorkerLifecycleTests
{
    [Fact]
    public async Task Worker_Standby_WhenSlotNotActive_DoesNotAcquireLease()
    {
        var slotCheckTcs = new TaskCompletionSource<bool>();
        var guardMock = new Mock<IWorkerActivationGuard>();
        guardMock.Setup(g => g.SlotColor).Returns("green");
        guardMock.Setup(g => g.Status).Returns(WorkerActivationStatus.Standby);
        guardMock.Setup(g => g.IsActiveSlotAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                slotCheckTcs.TrySetResult(true);
                return Task.FromResult(false);
            });

        var leaseMock = new Mock<IWorkerLeaseManager>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var worker = new WorkerService(guardMock.Object, leaseMock.Object, config, NullLogger<WorkerService>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        var checkedSlot = await Task.WhenAny(slotCheckTcs.Task, Task.Delay(3000));
        Assert.Equal(slotCheckTcs.Task, checkedSlot);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(worker.IsLeader);
        leaseMock.Verify(l => l.TryAcquireLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Worker_Active_AcquiresLease_And_Renews()
    {
        var guardMock = new Mock<IWorkerActivationGuard>();
        guardMock.Setup(g => g.SlotColor).Returns("blue");
        guardMock.Setup(g => g.Status).Returns(WorkerActivationStatus.Active);
        guardMock.Setup(g => g.IsActiveSlotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var renewalTcs = new TaskCompletionSource<bool>();
        var leaseMock = new Mock<IWorkerLeaseManager>();
        leaseMock.Setup(l => l.TryAcquireLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        leaseMock.Setup(l => l.RenewLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                renewalTcs.TrySetResult(true);
                return Task.FromResult(true);
            });
        leaseMock.Setup(l => l.ReleaseLeaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WORKER_LEASE_RENEW_INTERVAL_SECONDS"] = "0"
            })
            .Build();
        var worker = new WorkerService(guardMock.Object, leaseMock.Object, config, NullLogger<WorkerService>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        var renewed = await Task.WhenAny(renewalTcs.Task, Task.Delay(3000));
        Assert.Equal(renewalTcs.Task, renewed);
        Assert.True(worker.IsLeader);

        await worker.StopAsync(CancellationToken.None);
        Assert.False(worker.IsLeader);
        leaseMock.Verify(l => l.ReleaseLeaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Worker_Active_WhenLeaseAcquisitionFails_WaitsInStandby()
    {
        var guardMock = new Mock<IWorkerActivationGuard>();
        guardMock.Setup(g => g.SlotColor).Returns("blue");
        guardMock.Setup(g => g.Status).Returns(WorkerActivationStatus.Active);
        guardMock.Setup(g => g.IsActiveSlotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var leaseAttemptTcs = new TaskCompletionSource<bool>();
        var leaseMock = new Mock<IWorkerLeaseManager>();
        leaseMock.Setup(l => l.TryAcquireLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                leaseAttemptTcs.TrySetResult(true);
                return Task.FromResult(false);
            });

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var worker = new WorkerService(guardMock.Object, leaseMock.Object, config, NullLogger<WorkerService>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        var attempted = await Task.WhenAny(leaseAttemptTcs.Task, Task.Delay(3000));
        Assert.Equal(leaseAttemptTcs.Task, attempted);
        Assert.False(worker.IsLeader);

        await worker.StopAsync(CancellationToken.None);
        leaseMock.Verify(l => l.RenewLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Worker_Leader_WhenRenewalFails_LosesLeadership()
    {
        var guardMock = new Mock<IWorkerActivationGuard>();
        guardMock.Setup(g => g.SlotColor).Returns("blue");
        guardMock.Setup(g => g.Status).Returns(WorkerActivationStatus.Active);
        guardMock.Setup(g => g.IsActiveSlotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var leaseMock = new Mock<IWorkerLeaseManager>();
        leaseMock.Setup(l => l.TryAcquireLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var renewalAttemptTcs = new TaskCompletionSource<bool>();
        leaseMock.Setup(l => l.RenewLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                renewalAttemptTcs.TrySetResult(true);
                return Task.FromResult(false);
            });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WORKER_LEASE_RENEW_INTERVAL_SECONDS"] = "0"
            })
            .Build();

        var worker = new WorkerService(guardMock.Object, leaseMock.Object, config, NullLogger<WorkerService>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        var renewalFired = await Task.WhenAny(renewalAttemptTcs.Task, Task.Delay(3000));
        Assert.Equal(renewalAttemptTcs.Task, renewalFired);

        await worker.StopAsync(CancellationToken.None);
        Assert.False(worker.IsLeader);
    }

    [Fact]
    public async Task Worker_Leader_WhenSlotDemoted_ReleasesLease()
    {
        var guardMock = new Mock<IWorkerActivationGuard>();
        guardMock.Setup(g => g.SlotColor).Returns("blue");
        guardMock.Setup(g => g.Status).Returns(WorkerActivationStatus.Active);

        var callCount = 0;
        guardMock.Setup(g => g.IsActiveSlotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() =>
        {
            callCount++;
            return callCount == 1; // Active on first call, demoted to false on second call
        });

        var releaseTcs = new TaskCompletionSource<bool>();
        var leaseMock = new Mock<IWorkerLeaseManager>();
        leaseMock.Setup(l => l.TryAcquireLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        leaseMock.Setup(l => l.RenewLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        leaseMock.Setup(l => l.ReleaseLeaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                releaseTcs.TrySetResult(true);
                return Task.CompletedTask;
            });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WORKER_LEASE_RENEW_INTERVAL_SECONDS"] = "0"
            })
            .Build();

        var worker = new WorkerService(guardMock.Object, leaseMock.Object, config, NullLogger<WorkerService>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        var released = await Task.WhenAny(releaseTcs.Task, Task.Delay(3000));
        Assert.Equal(releaseTcs.Task, released);

        await worker.StopAsync(CancellationToken.None);
        Assert.False(worker.IsLeader);
        leaseMock.Verify(l => l.ReleaseLeaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Worker_Leader_WhenExceptionInLoop_ReleasesLeaseAndResetsLeadership()
    {
        var guardMock = new Mock<IWorkerActivationGuard>();
        guardMock.Setup(g => g.SlotColor).Returns("blue");
        guardMock.Setup(g => g.Status).Returns(WorkerActivationStatus.Active);
        guardMock.Setup(g => g.IsActiveSlotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var releaseTcs = new TaskCompletionSource<bool>();
        var leaseMock = new Mock<IWorkerLeaseManager>();
        leaseMock.Setup(l => l.TryAcquireLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        leaseMock.Setup(l => l.RenewLeaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated loop crash"));
        leaseMock.Setup(l => l.ReleaseLeaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                releaseTcs.TrySetResult(true);
                return Task.CompletedTask;
            });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WORKER_LEASE_RENEW_INTERVAL_SECONDS"] = "0"
            })
            .Build();

        var worker = new WorkerService(guardMock.Object, leaseMock.Object, config, NullLogger<WorkerService>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        var released = await Task.WhenAny(releaseTcs.Task, Task.Delay(3000));
        Assert.Equal(releaseTcs.Task, released);

        await worker.StopAsync(CancellationToken.None);
        Assert.False(worker.IsLeader);
    }
}
