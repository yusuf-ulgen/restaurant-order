using RestaurantOrder.Worker.Safety;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class WorkerHealthIndicatorTests
{
    private class FakeGuard : IWorkerActivationGuard
    {
        public string SlotColor { get; set; } = "blue";
        public string ActiveSlot { get; set; } = "blue";
        public WorkerActivationStatus Status { get; set; } = WorkerActivationStatus.Active;

        public Task<bool> IsActiveSlotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Status == WorkerActivationStatus.Active);
        }
    }

    [Fact]
    public void WorkerHealthIndicator_Reflects_GuardState_And_Tracks_Heartbeat()
    {
        var guard = new FakeGuard
        {
            SlotColor = "green",
            ActiveSlot = "green",
            Status = WorkerActivationStatus.Active
        };

        var indicator = new WorkerHealthIndicator(guard);

        Assert.Equal("green", indicator.SlotColor);
        Assert.Equal("green", indicator.ActiveSlot);
        Assert.Equal(WorkerActivationStatus.Active, indicator.CurrentStatus);

        var initialHeartbeat = indicator.LastHeartbeatUtc;
        Assert.True(initialHeartbeat <= DateTime.UtcNow);

        // Record heartbeat updates timestamp
        Thread.Sleep(5);
        indicator.RecordHeartbeat();
        Assert.True(indicator.LastHeartbeatUtc >= initialHeartbeat);
    }

    [Fact]
    public void WorkerHealthIndicator_Reflects_Standby_Status()
    {
        var guard = new FakeGuard
        {
            SlotColor = "blue",
            ActiveSlot = "green",
            Status = WorkerActivationStatus.Standby
        };

        var indicator = new WorkerHealthIndicator(guard);

        Assert.Equal("blue", indicator.SlotColor);
        Assert.Equal("green", indicator.ActiveSlot);
        Assert.Equal(WorkerActivationStatus.Standby, indicator.CurrentStatus);
    }
}
