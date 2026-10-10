using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Immutable audit aggregate recording dining session table transfers and session merges.
/// Movement history is strictly append-only; update and delete operations are prohibited.
/// </summary>
public sealed class DiningSessionMovement
{
    public const int MaxReasonLength = 500;
    public const int MaxIdempotencyKeyLength = 100;
    public const int MaxActorRoleLength = 50;

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public DiningSessionId SourceSessionId { get; private set; }
    public RestaurantTableId SourceTableId { get; private set; }
    public DiningSessionId? TargetSessionId { get; private set; }
    public RestaurantTableId TargetTableId { get; private set; }
    public DiningSessionMovementType MovementType { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorRole { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int GuestsMoved { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // Parameterless constructor for EF Core materialization
    private DiningSessionMovement()
    {
    }

    /// <summary>
    /// Creates an immutable audit record for transferring a session to another table.
    /// </summary>
    public static DiningSessionMovement CreateTransfer(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        RestaurantTableId sourceTableId,
        RestaurantTableId targetTableId,
        int guestsMoved,
        Guid actorId,
        string actorRole,
        string idempotencyKey,
        string? reason = null,
        DateTimeOffset? nowUtc = null)
    {
        if (sourceTableId == targetTableId)
        {
            throw new DomainException("Source and target table must be different for a table transfer.");
        }

        if (guestsMoved <= 0)
        {
            throw new DomainException("Guests moved count must be greater than zero.");
        }

        var sanitizedKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? throw new DomainException("Idempotency key is required for session movement.")
            : idempotencyKey.Trim();

        if (sanitizedKey.Length > MaxIdempotencyKeyLength)
        {
            throw new DomainException($"Idempotency key cannot exceed {MaxIdempotencyKeyLength} characters.");
        }

        var sanitizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (sanitizedReason != null && sanitizedReason.Length > MaxReasonLength)
        {
            throw new DomainException($"Reason cannot exceed {MaxReasonLength} characters.");
        }

        return new DiningSessionMovement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            SourceSessionId = sessionId,
            SourceTableId = sourceTableId,
            TargetSessionId = sessionId,
            TargetTableId = targetTableId,
            MovementType = DiningSessionMovementType.Transfer,
            GuestsMoved = guestsMoved,
            ActorId = actorId,
            ActorRole = string.IsNullOrWhiteSpace(actorRole) ? "Staff" : actorRole.Trim(),
            Reason = sanitizedReason,
            IdempotencyKey = sanitizedKey,
            CreatedAtUtc = nowUtc ?? DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates an immutable audit record for merging one session into another.
    /// </summary>
    public static DiningSessionMovement CreateMerge(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sourceSessionId,
        RestaurantTableId sourceTableId,
        DiningSessionId targetSessionId,
        RestaurantTableId targetTableId,
        int guestsMoved,
        Guid actorId,
        string actorRole,
        string idempotencyKey,
        string? reason = null,
        DateTimeOffset? nowUtc = null)
    {
        if (sourceSessionId == targetSessionId)
        {
            throw new DomainException("Source and target session must be different for a session merge.");
        }

        if (guestsMoved <= 0)
        {
            throw new DomainException("Guests moved count must be greater than zero.");
        }

        var sanitizedKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? throw new DomainException("Idempotency key is required for session movement.")
            : idempotencyKey.Trim();

        if (sanitizedKey.Length > MaxIdempotencyKeyLength)
        {
            throw new DomainException($"Idempotency key cannot exceed {MaxIdempotencyKeyLength} characters.");
        }

        var sanitizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (sanitizedReason != null && sanitizedReason.Length > MaxReasonLength)
        {
            throw new DomainException($"Reason cannot exceed {MaxReasonLength} characters.");
        }

        return new DiningSessionMovement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            SourceSessionId = sourceSessionId,
            SourceTableId = sourceTableId,
            TargetSessionId = targetSessionId,
            TargetTableId = targetTableId,
            MovementType = DiningSessionMovementType.Merge,
            GuestsMoved = guestsMoved,
            ActorId = actorId,
            ActorRole = string.IsNullOrWhiteSpace(actorRole) ? "Staff" : actorRole.Trim(),
            Reason = sanitizedReason,
            IdempotencyKey = sanitizedKey,
            CreatedAtUtc = nowUtc ?? DateTimeOffset.UtcNow
        };
    }
}
