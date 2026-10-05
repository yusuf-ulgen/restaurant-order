namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Validates whether a dining session is actively open for customer principal requests.
/// Enforces fail-closed token rejection when a dining session is closed.
/// </summary>
public interface ICustomerSessionValidator
{
    Task<bool> ValidateSessionActiveAsync(Guid tableSessionId, CancellationToken ct = default);
}
