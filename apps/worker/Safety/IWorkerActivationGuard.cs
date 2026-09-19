namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Guard interface to prevent concurrent worker execution during blue-green deployment.
/// </summary>
public interface IWorkerActivationGuard
{
    /// <summary>
    /// The deployment color assigned to this worker instance (e.g., 'blue' or 'green').
    /// </summary>
    string SlotColor { get; }

    /// <summary>
    /// The currently active production deployment slot authorized to execute jobs.
    /// </summary>
    string ActiveSlot { get; }

    /// <summary>
    /// The current operational activation status.
    /// </summary>
    WorkerActivationStatus Status { get; }

    /// <summary>
    /// Evaluates whether this worker instance is authorized to process work.
    /// Fails closed if the active slot cannot be determined.
    /// </summary>
    Task<bool> IsActiveSlotAsync(CancellationToken cancellationToken = default);
}
