namespace RestaurantOrder.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RestaurantOrder background worker host started safely.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Safe starter host: no business side effects, waits cleanly for shutdown
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("RestaurantOrder background worker host stopped gracefully.");
    }
}
