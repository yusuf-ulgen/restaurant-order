namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Operational lifecycle status of a restaurant Branch.
/// </summary>
public enum BranchStatus
{
    /// <summary>
    /// Branch is operational, can seat guests, take orders, and process payments.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Branch is temporarily suspended (e.g. renovation, seasonal closure, or administrative pause).
    /// </summary>
    Suspended = 2,

    /// <summary>
    /// Branch is permanently closed. Terminal state; no reactivation permitted.
    /// </summary>
    Closed = 3
}
