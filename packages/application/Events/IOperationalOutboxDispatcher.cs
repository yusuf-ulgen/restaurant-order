using System;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantOrder.Application.Events;

/// <summary>
/// Dispatcher service that claims pending operational outbox messages using distributed leases
/// and dispatches them to the real-time event publisher.
/// </summary>
public interface IOperationalOutboxDispatcher
{
    Task<int> DispatchPendingBatchAsync(
        int batchSize = 25,
        Func<CancellationToken, Task<bool>>? isLeaseValid = null,
        Guid? tenantId = null,
        CancellationToken ct = default);
}
