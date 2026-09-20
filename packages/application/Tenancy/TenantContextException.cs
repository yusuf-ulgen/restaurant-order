namespace RestaurantOrder.Application.Tenancy;

/// <summary>
/// Exception thrown when a tenant-scoped operation is attempted without a valid, authenticated tenant context.
/// </summary>
public class TenantContextException : Exception
{
    public TenantContextException(string message) : base(message)
    {
    }

    public TenantContextException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
