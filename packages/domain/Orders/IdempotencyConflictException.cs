using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Orders;

/// <summary>
/// Thrown when an incoming order submission uses an Idempotency-Key that has already been
/// recorded for a different request payload or a different dining session.
/// </summary>
public sealed class IdempotencyConflictException : DomainException
{
    public const string ErrorCode = "IDEMPOTENCY_CONFLICT";

    public IdempotencyConflictException(string message = "The provided Idempotency-Key has already been used for a different order payload or session.")
        : base(message)
    {
    }
}
