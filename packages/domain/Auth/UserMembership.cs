using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model for a user's membership in a specific tenant and branch.
/// Enforces multi-tenant role-to-scope pairing constraints.
/// </summary>
public sealed class UserMembership
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public UserId UserId { get; private set; }
    public AuthRole Role { get; private set; }
    public BranchId? BranchId { get; private set; }
    public UserMembershipStatus Status { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    private UserMembership()
    {
        // Required by EF Core
    }

    public static UserMembership Create(
        TenantId tenantId,
        UserId userId,
        AuthRole role,
        BranchId? branchId,
        DateTimeOffset nowUtc,
        Guid? id = null,
        UserMembershipStatus initialStatus = UserMembershipStatus.Active)
    {
        ValidateRoleAndScope(role, branchId);

        return new UserMembership
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            Role = role,
            BranchId = branchId,
            Status = initialStatus,
            IsActive = initialStatus == UserMembershipStatus.Active,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            ConcurrencyToken = Guid.CreateVersion7()
        };
    }

    public void ChangeRole(AuthRole newRole, BranchId? newBranchId, DateTimeOffset nowUtc)
    {
        ValidateRoleAndScope(newRole, newBranchId);

        Role = newRole;
        BranchId = newBranchId;
        UpdatedAtUtc = nowUtc;
        ConcurrencyToken = Guid.CreateVersion7();
    }

    public void UpdateStatus(UserMembershipStatus status, DateTimeOffset nowUtc)
    {
        Status = status;
        IsActive = status == UserMembershipStatus.Active;
        UpdatedAtUtc = nowUtc;
        ConcurrencyToken = Guid.CreateVersion7();
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        UpdateStatus(UserMembershipStatus.Disabled, nowUtc);
    }

    public void Reactivate(DateTimeOffset nowUtc)
    {
        UpdateStatus(UserMembershipStatus.Active, nowUtc);
    }

    public void Suspend(DateTimeOffset nowUtc)
    {
        UpdateStatus(UserMembershipStatus.Suspended, nowUtc);
    }

    private static void ValidateRoleAndScope(AuthRole role, BranchId? branchId)
    {
        if (role == AuthRole.Customer)
        {
            throw new InvalidAuthorizationScopeException(
                "Customer role cannot be assigned via a tenant staff membership.");
        }

        if (role == AuthRole.SuperAdmin)
        {
            throw new InvalidAuthorizationScopeException(
                "SuperAdmin is a platform-scoped role and cannot be bound to a tenant membership.");
        }

        if (role == AuthRole.RestaurantAdmin)
        {
            if (branchId.HasValue)
            {
                throw new InvalidAuthorizationScopeException(
                    "RestaurantAdmin operates at Tenant scope and must not have an assigned BranchId.");
            }
        }
        else if (role.IsBranchRole())
        {
            if (!branchId.HasValue)
            {
                throw new InvalidAuthorizationScopeException(
                    $"Role '{role}' is a branch-scoped role and requires an assigned BranchId.");
            }
        }
    }
}
