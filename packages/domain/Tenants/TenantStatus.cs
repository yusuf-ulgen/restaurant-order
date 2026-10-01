namespace RestaurantOrder.Domain.Tenants;

/// <summary>
/// Lifecycle status of a Tenant organization on the platform.
/// </summary>
public enum TenantStatus
{
    /// <summary>
    /// Tenant is operational; brands and branches can accept and process orders.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Tenant is temporarily suspended (e.g. overdue billing or operational pause).
    /// </summary>
    Suspended = 2,

    /// <summary>
    /// Tenant is permanently closed. Terminal state; no reactivation permitted.
    /// </summary>
    Closed = 3
}
