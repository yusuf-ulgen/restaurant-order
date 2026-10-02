using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Hosted background service that periodically triggers the outbox dispatcher to deliver
/// pending identity notifications (staff invitations, password resets).
/// </summary>
public sealed class IdentityNotificationOutboxBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<IdentityNotificationOutboxBackgroundService> _logger;

    public IdentityNotificationOutboxBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<IdentityNotificationOutboxBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var intervalSec = configuration.GetValue<int>("NOTIFICATION_DISPATCH_INTERVAL_SECONDS", 0);
        if (intervalSec <= 0)
        {
            intervalSec = configuration.GetValue<int>("Notification:DispatchIntervalSeconds", 5);
        }

        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, intervalSec));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Identity notification outbox dispatcher started with interval {IntervalSec}s.", _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IIdentityNotificationOutboxDispatcher>();
                await dispatcher.DispatchPendingBatchAsync(batchSize: 20, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in identity notification outbox background loop: {Message}", ex.Message);
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Identity notification outbox dispatcher stopped.");
    }
}
