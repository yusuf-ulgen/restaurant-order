namespace RestaurantOrder.Api.Tenancy;

/// <summary>
/// Endpoint metadata attribute declaring that an HTTP endpoint strictly requires
/// a valid, trusted tenant context.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class RequireTenantAttribute : Attribute
{
}
