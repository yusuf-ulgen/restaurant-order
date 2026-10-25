using System;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantOrder.Application.Notifications;

/// <summary>
/// Background dispatcher for persistent push notification delivery tasks.
/// Claims batches with PostgreSQL row locks (FOR UPDATE SKIP LOCKED),
/// enforces pre-send continuous authorization checks, and dispatches via IPushNotificationTransport.
/// </summary>
public interface IPushNotificationDispatcher
{
    Task<int> DispatchPendingBatchAsync(
        int batchSize = 25,
        Func<CancellationToken, Task<bool>>? isLeaseValid = null,
        Guid? tenantId = null,
        CancellationToken ct = default);
}
