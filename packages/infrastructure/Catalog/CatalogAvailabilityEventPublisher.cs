using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Catalog;

namespace RestaurantOrder.Infrastructure.Catalog;

/// <summary>
/// Default implementation of ICatalogAvailabilityEventPublisher.
/// Dispatches type-safe CatalogAvailabilityChangedEvent post-commit.
/// In Phase 9, this publisher will bridge to real-time notification transports (e.g. SignalR hub).
/// </summary>
public class CatalogAvailabilityEventPublisher : ICatalogAvailabilityEventPublisher
{
    private readonly ILogger<CatalogAvailabilityEventPublisher> _logger;

    public CatalogAvailabilityEventPublisher(ILogger<CatalogAvailabilityEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAvailabilityChangedAsync(CatalogAvailabilityChangedEvent @event, CancellationToken ct)
    {
        _logger.LogInformation(
            "Catalog availability changed: Tenant={TenantId}, Branch={BranchId}, Item={MenuItemId}, Variant={VariantId}, Available={IsAvailable}, Reason={ReasonCode}, OccurredAt={OccurredAtUtc}",
            @event.TenantId,
            @event.BranchId,
            @event.MenuItemId,
            @event.ItemVariantId,
            @event.IsAvailable,
            @event.ReasonCode,
            @event.OccurredAtUtc);

        return Task.CompletedTask;
    }
}
