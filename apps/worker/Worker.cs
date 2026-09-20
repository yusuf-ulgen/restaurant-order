using RestaurantOrder.Worker.Safety;

namespace RestaurantOrder.Worker;

/// <summary>
/// Background worker coordinator.
/// Enforces dual-layer safety before consuming queues, cron tasks, or printer spools:
/// 1. Verifies slot authorization via <see cref="IWorkerActivationGuard"/> (central Redis state).
/// 2. Acquires and renews an exclusive distributed lease via <see cref="IWorkerLeaseManager"/>.
/// If either condition is not met or lease renewal fails, the worker immediately halts consumption (fail-closed).
/// </summary>
public class Worker : BackgroundService
{
    private readonly IWorkerActivationGuard _activationGuard;
    private readonly IWorkerLeaseManager _leaseManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Worker> _logger;

    public bool IsLeader { get; private set; }

    public Worker(
        IWorkerActivationGuard activationGuard,
        IWorkerLeaseManager leaseManager,
        IConfiguration configuration,
        ILogger<Worker> logger)
    {
        _activationGuard = activationGuard;
        _leaseManager = leaseManager;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var leaseKey = _configuration["WORKER_LEASE_KEY"] ?? "restaurant-order:lease:worker-leadership";
        var leaseDuration = TimeSpan.FromSeconds(_configuration.GetValue("WORKER_LEASE_TTL_SECONDS", 15));
        var renewInterval = TimeSpan.FromSeconds(_configuration.GetValue("WORKER_LEASE_RENEW_INTERVAL_SECONDS", 5));
        var pollInterval = TimeSpan.FromSeconds(_configuration.GetValue("WORKER_STANDBY_POLL_INTERVAL_SECONDS", 5));

        _logger.LogInformation(
            "RestaurantOrder background worker initialized. Slot: '{Slot}', Status: '{Status}', LeaseKey: '{LeaseKey}'.",
            _activationGuard.SlotColor, _activationGuard.Status, leaseKey);

        string lastLoggedState = "";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var isActiveSlot = await _activationGuard.IsActiveSlotAsync(stoppingToken);

                if (!isActiveSlot)
                {
                    if (IsLeader)
                    {
                        _logger.LogWarning(
                            "[WORKER STANDBY] Slot '{Slot}' is no longer the active slot. Releasing leadership lease '{LeaseKey}'.",
                            _activationGuard.SlotColor, leaseKey);

                        await _leaseManager.ReleaseLeaseAsync(leaseKey, stoppingToken);
                        IsLeader = false;
                    }

                    if (lastLoggedState != "STANDBY_INACTIVE_SLOT")
                    {
                        lastLoggedState = "STANDBY_INACTIVE_SLOT";
                        _logger.LogInformation(
                            "[WORKER STANDBY] Slot '{Slot}' is in standby. Queue consumption and print jobs are PAUSED.",
                            _activationGuard.SlotColor);
                    }

                    await Task.Delay(pollInterval, stoppingToken);
                    continue;
                }

                // Slot is active: acquire or renew distributed lease
                if (!IsLeader)
                {
                    var acquired = await _leaseManager.TryAcquireLeaseAsync(leaseKey, leaseDuration, stoppingToken);
                    if (acquired)
                    {
                        IsLeader = true;
                        lastLoggedState = "ACTIVE_LEADER";
                        _logger.LogInformation(
                            "[WORKER ACTIVE] Slot '{Slot}' acquired distributed lease '{LeaseKey}'. Leadership is ACTIVE.",
                            _activationGuard.SlotColor, leaseKey);
                    }
                    else
                    {
                        if (lastLoggedState != "STANDBY_WAITING_LEASE")
                        {
                            lastLoggedState = "STANDBY_WAITING_LEASE";
                            _logger.LogInformation(
                                "[WORKER STANDBY] Slot '{Slot}' is active but another replica holds lease '{LeaseKey}'. Waiting...",
                                _activationGuard.SlotColor, leaseKey);
                        }

                        await Task.Delay(pollInterval, stoppingToken);
                        continue;
                    }
                }
                else
                {
                    // Existing leader: renew lease before expiry
                    var renewed = await _leaseManager.RenewLeaseAsync(leaseKey, leaseDuration, stoppingToken);
                    if (!renewed)
                    {
                        IsLeader = false;
                        lastLoggedState = "LEASE_LOST";
                        _logger.LogCritical(
                            "[WORKER LEASE LOST] Slot '{Slot}' failed to renew lease '{LeaseKey}'. Immediately halting job processing.",
                            _activationGuard.SlotColor, leaseKey);

                        await Task.Delay(pollInterval, stoppingToken);
                        continue;
                    }
                }

                // Active leader processing loop: work execution happens here
                await Task.Delay(renewInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Worker execution loop encountered an unhandled exception: {ExceptionType} - {Message}",
                    ex.GetType().Name, ex.Message);

                if (IsLeader)
                {
                    IsLeader = false;
                    try
                    {
                        await _leaseManager.ReleaseLeaseAsync(leaseKey, CancellationToken.None);
                    }
                    catch
                    {
                        // Best effort release
                    }
                }

                await Task.Delay(pollInterval, stoppingToken);
            }
        }

        // Graceful shutdown: release lease if currently held
        if (IsLeader)
        {
            _logger.LogInformation(
                "[WORKER SHUTDOWN] Slot '{Slot}' releasing leadership lease '{LeaseKey}' during graceful shutdown.",
                _activationGuard.SlotColor, leaseKey);

            try
            {
                await _leaseManager.ReleaseLeaseAsync(leaseKey, CancellationToken.None);
                IsLeader = false;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error releasing lease during shutdown: {Message}", ex.Message);
            }
        }

        _logger.LogInformation("RestaurantOrder background worker host stopped gracefully.");
    }
}
