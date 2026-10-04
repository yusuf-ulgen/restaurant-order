using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// MenuItemModifierGroupAssignment link entity assigning a ModifierGroup to a MenuItem with customized display sort order.
/// Enforces strict tenant and branch isolation.
/// </summary>
public class MenuItemModifierGroupAssignment
{
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public MenuId MenuId { get; private set; }
    public MenuItemId MenuItemId { get; private set; }
    public ModifierGroupId ModifierGroupId { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // Navigation properties for EF Core
    public MenuItem MenuItem { get; private set; } = null!;
    public ModifierGroup ModifierGroup { get; private set; } = null!;

    private MenuItemModifierGroupAssignment()
    {
    }

    public static MenuItemModifierGroupAssignment Create(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId menuItemId,
        ModifierGroupId modifierGroupId,
        int sortOrder = 0)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        return new MenuItemModifierGroupAssignment
        {
            TenantId = tenantId,
            BranchId = branchId,
            MenuId = menuId,
            MenuItemId = menuItemId,
            ModifierGroupId = modifierGroupId,
            SortOrder = sortOrder,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void UpdateSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        SortOrder = sortOrder;
    }
}
