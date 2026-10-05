using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Catalog;

public partial class ModifierGroup
{
    public ModifierOption AddOption(
        string name,
        PriceAmount priceDelta,
        int sortOrder = 0,
        bool isDefault = false,
        bool isActive = true)
    {
        var validatedName = ValidateOptionName(name);

        if (_options.Any(o => o.Name.Equals(validatedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"Option with name '{validatedName}' already exists in modifier group '{Name}'.");
        }

        if (isDefault && isActive)
        {
            var defaultCount = _options.Count(o => o.IsActive && o.IsDefault) + 1;
            if (defaultCount > MaxSelections)
            {
                throw new DomainException(
                    $"Number of active default options ({defaultCount}) exceeds MaxSelections ({MaxSelections}) for group '{Name}'.");
            }
        }

        var option = ModifierOption.Create(
            TenantId,
            BranchId,
            Id,
            validatedName,
            priceDelta,
            sortOrder,
            isDefault,
            isActive);

        _options.Add(option);
        Touch();
        return option;
    }

    public void AddOption(ModifierOption option)
    {
        if (_options.Any(o => o.Id != option.Id && o.Name.Equals(option.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"Option with name '{option.Name}' already exists in modifier group '{Name}'.");
        }

        if (option.IsDefault && option.IsActive)
        {
            var defaultCount = _options.Count(o => o.IsActive && o.IsDefault && o.Id != option.Id) + 1;
            if (defaultCount > MaxSelections)
            {
                throw new DomainException(
                    $"Number of active default options ({defaultCount}) exceeds MaxSelections ({MaxSelections}) for group '{Name}'.");
            }
        }

        _options.Add(option);
        Touch();
    }

    public void UpdateOptionDetails(ModifierOptionId optionId, string name, int sortOrder, bool isDefault)
    {
        var option = GetOption(optionId);
        var validatedName = ValidateOptionName(name);

        if (_options.Any(o => o.Id != optionId && o.Name.Equals(validatedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"Option with name '{validatedName}' already exists in modifier group '{Name}'.");
        }

        if (isDefault && option.IsActive)
        {
            var otherDefaults = _options.Count(o => o.Id != optionId && o.IsActive && o.IsDefault);
            if (otherDefaults + 1 > MaxSelections)
            {
                throw new DomainException(
                    $"Number of active default options ({otherDefaults + 1}) exceeds MaxSelections ({MaxSelections}) for group '{Name}'.");
            }
        }

        option.UpdateDetails(validatedName, sortOrder, isDefault);
        Touch();
    }

    public void UpdateOptionPrice(ModifierOptionId optionId, PriceAmount newPriceDelta)
    {
        var option = GetOption(optionId);
        option.UpdatePriceDelta(newPriceDelta);
        Touch();
    }

    public void ActivateOption(ModifierOptionId optionId)
    {
        var option = GetOption(optionId);
        if (option.IsDefault)
        {
            var otherDefaults = _options.Count(o => o.Id != optionId && o.IsActive && o.IsDefault);
            if (otherDefaults + 1 > MaxSelections)
            {
                throw new DomainException(
                    $"Activating default option exceeds MaxSelections ({MaxSelections}) for group '{Name}'.");
            }
        }

        option.Activate();
        Touch();
    }

    public void DeactivateOption(ModifierOptionId optionId, bool isAssigned = false)
    {
        var option = GetOption(optionId);
        if (!option.IsActive)
        {
            return;
        }

        var remainingActive = _options.Count(o => o.IsActive && o.Id != optionId);

        if (isAssigned && remainingActive == 0)
        {
            throw new DomainException(
                $"Cannot deactivate the last active option for assigned modifier group '{Name}'.");
        }

        if (IsActive || isAssigned)
        {
            if (remainingActive < MinSelections)
            {
                throw new DomainException(
                    $"Deactivating option would leave fewer active options ({remainingActive}) than MinSelections ({MinSelections}).");
            }

            if (remainingActive < MaxSelections)
            {
                throw new DomainException(
                    $"Deactivating option would leave fewer active options ({remainingActive}) than MaxSelections ({MaxSelections}).");
            }
        }

        option.Deactivate();
        Touch();
    }

    public void ReorderOptions(IReadOnlyList<(ModifierOptionId OptionId, int SortOrder)> orderings)
    {
        if (orderings == null || orderings.Count != _options.Count)
        {
            throw new DomainException($"Reorder list must contain all {_options.Count} options for this modifier group.");
        }

        var map = _options.ToDictionary(o => o.Id);
        var seenIds = new HashSet<ModifierOptionId>();
        var seenSortOrders = new HashSet<int>();

        foreach (var (optId, sortOrder) in orderings)
        {
            if (!seenIds.Add(optId))
            {
                throw new DomainException($"Duplicate modifier option ID '{optId.Value}' in reorder list.");
            }

            if (!map.TryGetValue(optId, out var option))
            {
                throw new DomainException($"Modifier option '{optId.Value}' does not belong to this modifier group.");
            }

            if (sortOrder < 0)
            {
                throw new DomainException("Sort order must be non-negative.");
            }

            if (!seenSortOrders.Add(sortOrder))
            {
                throw new DomainException($"Duplicate sort order value '{sortOrder}' in reorder list.");
            }
        }

        foreach (var (optId, sortOrder) in orderings)
        {
            map[optId].UpdateSortOrder(sortOrder);
        }

        Touch();
    }

    public void ValidateOptionsInvariant()
    {
        var activeOptionsCount = _options.Count(o => o.IsActive);
        if (_options.Count > 0 && MaxSelections > activeOptionsCount)
        {
            throw new DomainException(
                $"MaxSelections ({MaxSelections}) cannot exceed total active options ({activeOptionsCount}) for group '{Name}'.");
        }

        var defaultCount = _options.Count(o => o.IsActive && o.IsDefault);
        if (defaultCount > MaxSelections)
        {
            throw new DomainException(
                $"Number of active default options ({defaultCount}) exceeds MaxSelections ({MaxSelections}) for group '{Name}'.");
        }
    }

    public void ValidateForAssignment()
    {
        if (!IsActive)
        {
            throw new DomainException($"Cannot assign inactive modifier group '{Name}'.");
        }

        var activeOptionsCount = _options.Count(o => o.IsActive);
        if (activeOptionsCount < MaxSelections)
        {
            throw new DomainException(
                $"Modifier group '{Name}' has MaxSelections={MaxSelections} but only {activeOptionsCount} active options.");
        }

        ValidateOptionsInvariant();
    }

    private ModifierOption GetOption(ModifierOptionId optionId)
    {
        return _options.FirstOrDefault(o => o.Id == optionId)
            ?? throw new DomainException($"ModifierOption '{optionId.Value}' does not belong to group '{Name}'.");
    }

    private static string ValidateOptionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Modifier option name cannot be empty.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 100)
        {
            throw new DomainException("Modifier option name must be between 1 and 100 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Modifier option name cannot contain HTML or markup tags.");
        }

        return trimmed;
    }
}
