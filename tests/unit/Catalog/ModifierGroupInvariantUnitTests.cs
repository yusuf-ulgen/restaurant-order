using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Catalog;

public class ModifierGroupInvariantUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();

    private ModifierGroup CreateGroup(int min = 1, int max = 2, int optionCount = 3)
    {
        var group = ModifierGroup.Create(_tenantId, _branchId, "Sauces", min, max, 0);
        for (int i = 0; i < optionCount; i++)
        {
            group.AddOption($"Option {i + 1}", PriceAmount.FromMinorUnits(i * 50), sortOrder: i);
        }
        return group;
    }

    [Fact]
    public void DeactivateOption_WhenRemainingActiveLessThanMaxSelections_ThrowsDomainException()
    {
        // 3 options, min 0, max 3
        var group = CreateGroup(min: 0, max: 3, optionCount: 3);
        var firstOptionId = group.Options[0].Id;

        // Deactivating 1 option leaves 2 active, which is < MaxSelections (3)
        var ex = Assert.Throws<DomainException>(() => group.DeactivateOption(firstOptionId, isAssigned: true));
        Assert.Contains("MaxSelections", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeactivateOption_WhenRemainingActiveLessThanMinSelections_ThrowsDomainException()
    {
        // 3 options, min is 3, max is 3
        var group = CreateGroup(min: 3, max: 3, optionCount: 3);
        var firstOptionId = group.Options[0].Id;

        var ex = Assert.Throws<DomainException>(() => group.DeactivateOption(firstOptionId, isAssigned: true));
        Assert.Contains("MinSelections", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeactivateOption_WhenAssignedGroupBecomesEmpty_ThrowsDomainException()
    {
        // 1 option, min 0, max 1
        var group = CreateGroup(min: 0, max: 1, optionCount: 1);
        var optionId = group.Options[0].Id;

        // Deactivating the last active option on an assigned group must be rejected
        var ex = Assert.Throws<DomainException>(() => group.DeactivateOption(optionId, isAssigned: true));
        Assert.Contains("Cannot deactivate the last active option", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeactivateOption_WhenUnassignedDraftGroupBecomesEmpty_IsAllowed()
    {
        // Unassigned draft group can have all options deactivated while configuring
        var group = CreateGroup(min: 0, max: 0, optionCount: 1);
        var optionId = group.Options[0].Id;

        group.DeactivateOption(optionId, isAssigned: false);
        Assert.False(group.Options[0].IsActive);
    }

    [Fact]
    public void UpdateDetails_WhenMaxSelectionsExceedsActiveOptionsOnAssignedGroup_ThrowsDomainException()
    {
        // 2 options
        var group = CreateGroup(min: 0, max: 1, optionCount: 2);

        // Increasing MaxSelections to 5 when there are only 2 options on assigned group must be rejected
        var ex = Assert.Throws<DomainException>(() => group.UpdateDetails("Sauces", minSelections: 0, maxSelections: 5, sortOrder: 0, isAssigned: true));
        Assert.Contains("cannot exceed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateDetails_WhenMinSelectionsExceedsMaxSelections_ThrowsDomainException()
    {
        var group = CreateGroup(min: 0, max: 1, optionCount: 2);

        var ex = Assert.Throws<DomainException>(() => group.UpdateDetails("Sauces", minSelections: 3, maxSelections: 2, sortOrder: 0, isAssigned: false));
        Assert.Contains("cannot exceed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateDetails_WhenMinSelectionsNegative_ThrowsDomainException()
    {
        var group = CreateGroup(min: 0, max: 1, optionCount: 2);

        var ex = Assert.Throws<DomainException>(() => group.UpdateDetails("Sauces", minSelections: -1, maxSelections: 2, sortOrder: 0, isAssigned: false));
        Assert.Contains("cannot be negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
