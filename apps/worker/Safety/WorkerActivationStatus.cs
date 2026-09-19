namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Status indicating whether the worker instance is authorized to process jobs.
/// </summary>
public enum WorkerActivationStatus
{
    /// <summary>
    /// Worker is the active slot and authorized to process queues, cron, and printing.
    /// </summary>
    Active,

    /// <summary>
    /// Worker is in an idle/standby slot during blue-green deployment.
    /// It must NOT process queues, cron jobs, or thermal print spools.
    /// </summary>
    Standby,

    /// <summary>
    /// Worker has been explicitly disabled via configuration kill-switch.
    /// </summary>
    Disabled,

    /// <summary>
    /// Worker could not determine its activation state or configuration was invalid.
    /// Fail-closed: worker must NOT process any work.
    /// </summary>
    Error
}
