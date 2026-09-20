using System.Text.RegularExpressions;

namespace RestaurantOrder.Application.Tenancy;

/// <summary>
/// Centralized factory for generating Redis cache keys following the enterprise pattern:
/// cache:{tenant_id}:{branch_id}:{resource}:{id}
/// Strictly prevents Redis key injection by rejecting control characters, newlines, and colons.
/// </summary>
public static class TenantCacheKeyFactory
{
    public const string GlobalBranchScope = "_global";

    private static readonly Regex SafeTokenRegex = new(
        @"^[a-zA-Z0-9_\-\.]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Creates a cache key for a tenant- and branch-scoped resource.
    /// Format: cache:{tenant_id}:{branch_id}:{resource}:{id}
    /// </summary>
    public static string Create(Guid tenantId, Guid? branchId, string resource, string id)
    {
        ValidateTenantId(tenantId);
        ValidateToken("resource", resource);
        ValidateToken("id", id);

        var branchScope = branchId.HasValue && branchId.Value != Guid.Empty
            ? branchId.Value.ToString("D")
            : GlobalBranchScope;

        return $"cache:{tenantId:D}:{branchScope}:{resource.ToLowerInvariant()}:{id}";
    }

    /// <summary>
    /// Creates a cache key for a tenant-wide resource where branch scope is not applicable.
    /// Format: cache:{tenant_id}:_global:{resource}:{id}
    /// </summary>
    public static string CreateTenantScoped(Guid tenantId, string resource, string id)
    {
        return Create(tenantId, null, resource, id);
    }

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID is mandatory for tenant-scoped cache keys.", nameof(tenantId));
        }
    }

    private static void ValidateToken(string paramName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Cache key component '{paramName}' cannot be null, empty, or whitespace.", paramName);
        }

        // Check for control characters or colons that could cause key injection / split
        if (value.IndexOfAny(new[] { ':', '\r', '\n', '\0', ' ', '\t' }) >= 0)
        {
            throw new ArgumentException(
                $"Cache key component '{paramName}' contains forbidden characters (colons, newlines, null bytes, or whitespace).",
                paramName);
        }

        if (!SafeTokenRegex.IsMatch(value))
        {
            throw new ArgumentException(
                $"Cache key component '{paramName}' contains invalid characters. Only alphanumeric, underscore, hyphen, and dot are permitted.",
                paramName);
        }
    }
}
