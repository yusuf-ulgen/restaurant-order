using RestaurantOrder.Worker.Safety;

namespace RestaurantOrder.Worker;

public class Worker : BackgroundService
{
    private readonly IWorkerActivationGuard _activationGuard;
    private readonly ILogger<Worker> _logger;

    public Worker(IWorkerActivationGuard activationGuard, ILogger<Worker> logger)
    {
        _activationGuard = activationGuard;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RestaurantOrder background worker initialized. Slot: '{Slot}', Status: '{Status}'.",
            _activationGuard.SlotColor, _activationGuard.Status);

        while (!stoppingToken.IsCancellationRequested)
        {
            var isActive = await _activationGuard.IsActiveSlotAsync(stoppingToken);

            if (!isActive)
            {
                _logger.LogInformation(
                    "[WORKER STANDBY] Slot '{SlotColor}' is in '{Status}' mode (Active slot: '{ActiveSlot}'). Queue consumption and print jobs are PAUSED.",
                    _activationGuard.SlotColor, _activationGuard.Status, _activationGuard.ActiveSlot);
            }
            else
            {
                _logger.LogInformation(
                    "[WORKER ACTIVE] Slot '{SlotColor}' is ACTIVE. Ready to process queues and scheduled jobs.",
                    _activationGuard.SlotColor);
            }

            try
            {
                // Evaluation loop: poll state every 30 seconds
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("RestaurantOrder background worker host stopped gracefully.");
    }
}
