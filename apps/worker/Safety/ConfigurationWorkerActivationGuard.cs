namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Configuration-driven worker activation guard.
/// Evaluates DEPLOYMENT_COLOR against ACTIVE_DEPLOYMENT_SLOT to ensure only
/// the active blue-green slot consumes queues, scheduled cron jobs, and printer spools.
/// In Staging/Production, fails closed if configuration is missing or malformed.
/// </summary>
public class ConfigurationWorkerActivationGuard : IWorkerActivationGuard
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ConfigurationWorkerActivationGuard> _logger;

    public ConfigurationWorkerActivationGuard(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<ConfigurationWorkerActivationGuard> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public string SlotColor => ResolveSlotColor();

    public string ActiveSlot => ResolveActiveSlot();

    public WorkerActivationStatus Status => EvaluateStatus();

    public Task<bool> IsActiveSlotAsync(CancellationToken cancellationToken = default)
    {
        var isAuthorized = Status == WorkerActivationStatus.Active;
        return Task.FromResult(isAuthorized);
    }

    private string ResolveSlotColor()
    {
        var color = _configuration["DEPLOYMENT_COLOR"];
        if (string.IsNullOrWhiteSpace(color))
        {
            return _environment.IsDevelopment() ? "blue" : "unconfigured";
        }
        return color.Trim().ToLowerInvariant();
    }

    private string ResolveActiveSlot()
    {
        var active = _configuration["ACTIVE_DEPLOYMENT_SLOT"];
        if (string.IsNullOrWhiteSpace(active))
        {
            return _environment.IsDevelopment() ? ResolveSlotColor() : "unconfigured";
        }
        return active.Trim().ToLowerInvariant();
    }

    private WorkerActivationStatus EvaluateStatus()
    {
        var enabled = _configuration.GetValue("WORKER_ENABLED", true);
        if (!enabled)
        {
            return WorkerActivationStatus.Disabled;
        }

        var slotColor = ResolveSlotColor();
        var activeSlot = ResolveActiveSlot();

        var isStrictEnv = _environment.IsProduction() || _environment.IsEnvironment("Staging");

        if (isStrictEnv)
        {
            if (slotColor == "unconfigured" || activeSlot == "unconfigured" ||
                (slotColor != "blue" && slotColor != "green") ||
                (activeSlot != "blue" && activeSlot != "green"))
            {
                _logger.LogCritical(
                    "Worker activation guard failed closed. Invalid configuration: DEPLOYMENT_COLOR='{SlotColor}', ACTIVE_DEPLOYMENT_SLOT='{ActiveSlot}'.",
                    slotColor, activeSlot);
                return WorkerActivationStatus.Error;
            }
        }

        if (string.Equals(slotColor, activeSlot, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerActivationStatus.Active;
        }

        return WorkerActivationStatus.Standby;
    }
}
