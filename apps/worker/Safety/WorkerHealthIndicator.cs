namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Exposes worker health and operational status for diagnostics.
/// </summary>
public interface IWorkerHealthIndicator
{
    WorkerActivationStatus CurrentStatus { get; }
    string SlotColor { get; }
    string ActiveSlot { get; }
    DateTime LastHeartbeatUtc { get; }
}

public class WorkerHealthIndicator : IWorkerHealthIndicator
{
    private readonly IWorkerActivationGuard _activationGuard;

    public WorkerHealthIndicator(IWorkerActivationGuard activationGuard)
    {
        _activationGuard = activationGuard;
        LastHeartbeatUtc = DateTime.UtcNow;
    }

    public WorkerActivationStatus CurrentStatus => _activationGuard.Status;
    public string SlotColor => _activationGuard.SlotColor;
    public string ActiveSlot => _activationGuard.ActiveSlot;
    public DateTime LastHeartbeatUtc { get; private set; }

    public void RecordHeartbeat()
    {
        LastHeartbeatUtc = DateTime.UtcNow;
    }
}
