namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Contextual resource metadata used to verify own-or-assigned resource authorization boundaries.
/// Prevents roles with 'O' grant types from acting across unassigned or foreign resources.
/// </summary>
public sealed record ResourceOwnershipContext
{
    public Guid? OwnerStaffId { get; init; }
    public Guid? AssignedStaffId { get; init; }
    public Guid? TableSessionId { get; init; }
    public string? Station { get; init; }
    public Guid? RegisterSessionId { get; init; }
    public Guid? BranchId { get; init; }

    public static ResourceOwnershipContext ForBranch(Guid branchId) =>
        new() { BranchId = branchId };

    public static ResourceOwnershipContext ForTableSession(Guid tableSessionId) =>
        new() { TableSessionId = tableSessionId };

    public static ResourceOwnershipContext ForStaffAssignment(Guid staffId) =>
        new() { AssignedStaffId = staffId };

    public static ResourceOwnershipContext ForStaffOwnership(Guid staffId) =>
        new() { OwnerStaffId = staffId };

    public static ResourceOwnershipContext ForStation(string station) =>
        new() { Station = station };

    public static ResourceOwnershipContext ForCashierRegister(Guid cashierStaffId, Guid? registerSessionId = null) =>
        new() { OwnerStaffId = cashierStaffId, RegisterSessionId = registerSessionId };
}
