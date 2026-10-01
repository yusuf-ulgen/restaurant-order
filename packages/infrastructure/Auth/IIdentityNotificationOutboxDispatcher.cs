namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Dispatcher contract for processing pending identity notifications from outbox storage.
/// </summary>
public interface IIdentityNotificationOutboxDispatcher
{
    Task<int> DispatchPendingBatchAsync(int batchSize = 10, CancellationToken ct = default);
}
