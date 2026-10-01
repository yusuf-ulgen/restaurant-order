using Microsoft.AspNetCore.Authorization;

namespace RestaurantOrder.Api.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public string Permission { get; }

    public RequirePermissionAttribute(string permission)
        : base($"{PermissionPolicyProvider.PolicyPrefix}{permission}")
    {
        Permission = permission;
    }
}

public static class RequirePermissionExtensions
{
    /// <summary>
    /// Enforces that the caller holds the specified centralized permission capability.
    /// </summary>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission)
    {
        return builder.RequireAuthorization($"{PermissionPolicyProvider.PolicyPrefix}{permission}");
    }
}
