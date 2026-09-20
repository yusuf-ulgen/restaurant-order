namespace RestaurantOrder.Domain.Brands;

/// <summary>
/// Status of a Brand within a Tenant.
/// </summary>
public enum BrandStatus
{
    /// <summary>
    /// Brand is active and operational.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Brand is inactive / paused.
    /// </summary>
    Inactive = 2
}
