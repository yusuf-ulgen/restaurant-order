namespace RestaurantOrder.Application.Tenancy;

/// <summary>
/// Envelope contract for tenant-scoped background worker jobs.
/// Requires an explicit, non-empty TenantId and carries idempotency and job type metadata.
/// </summary>
public interface ITenantJobEnvelope
{
    Guid TenantId { get; }
    Guid? BranchId { get; }
    string JobType { get; }
    string IdempotencyKey { get; }
}
