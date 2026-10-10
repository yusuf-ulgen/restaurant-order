using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Aggregate root representing an in-person dining session at a physical restaurant table.
/// Governs guest occupancy, waiter assignment, and state machine transitions:
/// Open -> Active -> BillRequested -> Closed (terminal).
///
/// NOTE on Phase Hardening:
/// In Phase 6.2, dining session closing finalizes the floor occupancy lifecycle.
/// In Phase 8 (Order Core) and Phase 13 (Billing & Settlement), this transition
/// will be hardened to strictly enforce zero open orders and complete balance settlement.
/// </summary>
public class DiningSession
{
    public const int MaxCloseReasonLength = 500;

    public DiningSessionId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public RestaurantTableId TableId { get; private set; }

    public DiningSessionStatus Status { get; private set; }
    public int GuestCount { get; private set; }
    public Guid? AssignedWaiterId { get; private set; }

    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset? ActivatedAtUtc { get; private set; }
    public DateTimeOffset? BillRequestedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public string? CloseReason { get; private set; }
    public DiningSessionId? MergedIntoSessionId { get; private set; }

    public Guid ConcurrencyToken { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private DiningSession()
    {
    }

    /// <summary>
    /// Opens a new dining session at an active table.
    /// Validates guest count bounds against table capacity and ensures initial Open status.
    /// </summary>
    public static DiningSession Open(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTable table,
        int guestCount,
        Guid? assignedWaiterId = null,
        DiningSessionId? id = null)
    {
        if (table.TenantId != tenantId)
        {
            throw new DomainException("Table does not belong to the specified tenant.");
        }

        if (table.BranchId != branchId)
        {
            throw new DomainException("Table does not belong to the specified branch.");
        }

        if (!table.IsActive)
        {
            throw new DomainException("Cannot open dining session on an inactive table.");
        }

        if (guestCount <= 0)
        {
            throw new DomainException("Guest count must be greater than zero.");
        }

        if (guestCount > table.Capacity)
        {
            throw new DomainException(
                $"Guest count ({guestCount}) cannot exceed table capacity ({table.Capacity}).");
        }

        var now = DateTimeOffset.UtcNow;
        var sessionId = id ?? DiningSessionId.New();

        return new DiningSession
        {
            Id = sessionId,
            TenantId = tenantId,
            BranchId = branchId,
            TableId = table.Id,
            Status = DiningSessionStatus.Open,
            GuestCount = guestCount,
            AssignedWaiterId = assignedWaiterId,
            OpenedAtUtc = now,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAtUtc = now
        };
    }

    /// <summary>
    /// Transitions session from Open to Active.
    /// </summary>
    public void Activate()
    {
        if (Status == DiningSessionStatus.Active)
        {
            throw new DomainException("Dining session is already active.");
        }

        if (Status != DiningSessionStatus.Open)
        {
            throw new DomainException(
                $"Cannot activate dining session in status '{Status}'. Only Open sessions can be activated.");
        }

        var now = DateTimeOffset.UtcNow;
        Status = DiningSessionStatus.Active;
        ActivatedAtUtc = now;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>
    /// Transitions session from Active to BillRequested.
    /// </summary>
    public void RequestBill()
    {
        if (Status == DiningSessionStatus.BillRequested)
        {
            throw new DomainException("Bill has already been requested for this dining session.");
        }

        if (Status == DiningSessionStatus.Open)
        {
            throw new DomainException("Cannot request bill for a dining session that is not yet active.");
        }

        if (Status == DiningSessionStatus.Closed)
        {
            throw new DomainException("Cannot request bill for a closed dining session.");
        }

        var now = DateTimeOffset.UtcNow;
        Status = DiningSessionStatus.BillRequested;
        BillRequestedAtUtc = now;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>
    /// Transitions session from BillRequested to Closed (terminal state).
    /// </summary>
    public void Close(string? reason = null)
    {
        if (Status == DiningSessionStatus.Closed)
        {
            throw new DomainException("Dining session is already closed.");
        }

        if (Status != DiningSessionStatus.BillRequested)
        {
            throw new DomainException(
                $"Cannot close dining session in status '{Status}'. Bill must be requested before closing.");
        }

        var sanitizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (sanitizedReason != null && sanitizedReason.Length > MaxCloseReasonLength)
        {
            throw new DomainException(
                $"Close reason cannot exceed {MaxCloseReasonLength} characters.");
        }

        var now = DateTimeOffset.UtcNow;
        Status = DiningSessionStatus.Closed;
        ClosedAtUtc = now;
        CloseReason = sanitizedReason;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>
    /// Assigns or reassigns staff/waiter to this session.
    /// </summary>
    public void AssignWaiter(Guid? waiterId)
    {
        if (Status == DiningSessionStatus.Closed)
        {
            throw new DomainException("Cannot assign waiter to a closed dining session.");
        }

        AssignedWaiterId = waiterId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>
    /// Transfers an open or active dining session to another active table.
    /// Rejects closed sessions, sessions with bills requested, and target tables exceeding guest capacity.
    /// </summary>
    public void TransferToTable(RestaurantTable newTable)
    {
        if (Status == DiningSessionStatus.Closed)
        {
            throw new DomainException("Cannot transfer a closed dining session.");
        }

        if (Status == DiningSessionStatus.BillRequested)
        {
            throw new DomainException(
                "Cannot transfer dining session with bill requested. Bill requested sessions cannot be transferred.");
        }

        if (newTable.TenantId != TenantId)
        {
            throw new DomainException("Table does not belong to the specified tenant.");
        }

        if (newTable.BranchId != BranchId)
        {
            throw new DomainException("Table does not belong to the specified branch.");
        }

        if (newTable.Id == TableId)
        {
            throw new DomainException("Cannot transfer session to the same table.");
        }

        if (!newTable.IsActive)
        {
            throw new DomainException("Cannot transfer session to an inactive table.");
        }

        if (GuestCount > newTable.Capacity)
        {
            throw new DomainException(
                $"Guest count ({GuestCount}) exceeds target table capacity ({newTable.Capacity}).");
        }

        var now = DateTimeOffset.UtcNow;
        TableId = newTable.Id;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>
    /// Accepts merged guests into this target session from another session.
    /// Validates that combined guest count does not exceed target table capacity.
    /// </summary>
    public void AcceptMergedGuests(int additionalGuestCount, RestaurantTable targetTable)
    {
        if (Status == DiningSessionStatus.Closed)
        {
            throw new DomainException("Cannot merge into a closed dining session.");
        }

        if (Status == DiningSessionStatus.BillRequested)
        {
            throw new DomainException("Cannot merge into a dining session with bill requested.");
        }

        if (additionalGuestCount <= 0)
        {
            throw new DomainException("Additional guest count must be greater than zero.");
        }

        var combinedGuests = GuestCount + additionalGuestCount;
        if (combinedGuests > targetTable.Capacity)
        {
            throw new DomainException(
                $"Combined guest count ({combinedGuests}) exceeds target table capacity ({targetTable.Capacity}).");
        }

        var now = DateTimeOffset.UtcNow;
        GuestCount = combinedGuests;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>
    /// Merges this source session into a target session.
    /// Transitions this session to terminal Closed state with MergedIntoSessionId pointer.
    /// </summary>
    public void MergeInto(DiningSession targetSession, string? reason = null)
    {
        if (Status == DiningSessionStatus.Closed)
        {
            throw new DomainException("Dining session is already closed.");
        }

        if (Status == DiningSessionStatus.BillRequested)
        {
            throw new DomainException("Cannot merge a dining session with bill requested.");
        }

        if (targetSession.Id == Id)
        {
            throw new DomainException("Cannot merge dining session into itself.");
        }

        if (targetSession.TenantId != TenantId)
        {
            throw new DomainException("Target session belongs to a different tenant.");
        }

        if (targetSession.BranchId != BranchId)
        {
            throw new DomainException("Target session belongs to a different branch.");
        }

        if (targetSession.Status == DiningSessionStatus.Closed)
        {
            throw new DomainException("Target session is already closed.");
        }

        if (targetSession.Status == DiningSessionStatus.BillRequested)
        {
            throw new DomainException("Cannot merge into a target session with bill requested.");
        }

        var sanitizedReason = string.IsNullOrWhiteSpace(reason)
            ? $"Merged into session {targetSession.Id.Value}"
            : reason.Trim();

        if (sanitizedReason.Length > MaxCloseReasonLength)
        {
            throw new DomainException($"Close reason cannot exceed {MaxCloseReasonLength} characters.");
        }

        var now = DateTimeOffset.UtcNow;
        Status = DiningSessionStatus.Closed;
        ClosedAtUtc = now;
        CloseReason = sanitizedReason;
        MergedIntoSessionId = targetSession.Id;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }
}
