namespace RestaurantOrder.Application.Catalog;

/// <summary>
/// Dispatches catalog availability events post-commit.
/// </summary>
public interface ICatalogAvailabilityEventPublisher
{
    Task PublishAvailabilityChangedAsync(CatalogAvailabilityChangedEvent @event, CancellationToken ct);
}
