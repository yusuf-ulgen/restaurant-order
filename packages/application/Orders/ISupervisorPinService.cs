using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Orders;

/// <summary>
/// Authoritative atomic service for verifying supervisor PIN authorization
/// during sensitive staff operations (e.g., in-prep order item voiding/cancellation).
/// </summary>
public interface ISupervisorPinService
{
    /// <summary>
    /// Atomically validates supervisor PIN against locked credential in an isolated security transaction.
    /// Strictly restricts supervisor override to active BranchManager roles within the same tenant and branch.
    /// On wrong PIN, atomically records failed attempt and locks credential at 5 failures without fail-open.
    /// </summary>
    Task<(Guid SupervisorId, string SupervisorRole)> VerifySupervisorPinAsync(
        TenantId tenantId,
        BranchId branchId,
        Guid? supervisorUserId,
        string? supervisorPin,
        CancellationToken cancellationToken);
}
