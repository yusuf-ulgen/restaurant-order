namespace RestaurantOrder.Application.Tenancy;

/// <summary>
/// Provides access to the current scoped or ambient <see cref="ITenantContext"/>.
/// </summary>
public interface ITenantContextAccessor
{
    ITenantContext TenantContext { get; set; }
}
