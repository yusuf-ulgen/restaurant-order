namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Validates whether a selected branch belongs to the authenticated tenant.
/// Used for tenant-scoped actors (e.g. RestaurantAdmin) selecting a branch stream.
/// </summary>
public interface IBranchScopeValidator
{
    Task<bool> ValidateBranchBelongsToTenantAsync(Guid tenantId, Guid branchId, CancellationToken ct = default);
}
