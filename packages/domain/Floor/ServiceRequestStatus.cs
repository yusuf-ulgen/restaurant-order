namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Status lifecycle of a service request.
/// Phase 7 enforces Open and Cancelled states.
/// Acknowledged and Completed states are reserved for Phase 10 (Staff/Waitstaff operations).
/// </summary>
public enum ServiceRequestStatus
{
    /// <summary>
    /// The request has been submitted by the customer and is active.
    /// </summary>
    Open = 1,

    /// <summary>
    /// The request was cancelled by the customer before being acknowledged.
    /// </summary>
    Cancelled = 2,

    /// <summary>
    /// Reserved for Phase 10: A staff member has acknowledged receipt of the request.
    /// </summary>
    Acknowledged = 3,

    /// <summary>
    /// Reserved for Phase 10: The service request has been fulfilled and completed.
    /// </summary>
    Completed = 4
}
