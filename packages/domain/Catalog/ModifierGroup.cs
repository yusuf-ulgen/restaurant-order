using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// ModifierGroup aggregate root representing a reusable set of options
/// (e.g. "Salad Dressing", "Meat Temperature", "Extra Cheese").
/// Enforces min/max selection bounds, active default option invariants,
/// and unique option names within the group.
/// </summary>
public class ModifierGroup
{
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    private readonly List<ModifierOption> _options = new();

    public ModifierGroupId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public string Name { get; private set; } = null!;
    public int MinSelections { get; private set; }
    public int MaxSelections { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public IReadOnlyList<ModifierOption> Options => _options.AsReadOnly();

    private ModifierGroup()
    {
    }

    public static ModifierGroup Create(
        TenantId tenantId,
        BranchId branchId,
        string name,
        int minSelections,
        int maxSelections,
        int sortOrder = 0,
        bool isActive = true,
        ModifierGroupId? id = null)
    {
        var validatedName = ValidateName(name);
        ValidateSelectionBounds(minSelections, maxSelections);

        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        return new ModifierGroup
        {
            Id = id ?? ModifierGroupId.New(),
            TenantId = tenantId,
            BranchId = branchId,
            Name = validatedName,
            MinSelections = minSelections,
            MaxSelections = maxSelections,
            SortOrder = sortOrder,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
    }

    public void UpdateDetails(string name, int minSelections, int maxSelections, int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        var validatedName = ValidateName(name);
        ValidateSelectionBounds(minSelections, maxSelections);

        var activeOptionsCount = _options.Count(o => o.IsActive);
        if (activeOptionsCount > 0 && maxSelections > activeOptionsCount)
        {
            throw new DomainException(
                $"MaxSelections ({maxSelections}) cannot exceed total active options ({activeOptionsCount}) for group '{validatedName}'.");
        }

        var activeDefaultCount = _options.Count(o => o.IsActive && o.IsDefault);
        if (activeDefaultCount > maxSelections)
        {
            throw new DomainException(
                $"Number of active default options ({activeDefaultCount}) exceeds MaxSelections ({maxSelections}) for group '{validatedName}'.");
        }

        Name = validatedName;
        MinSelections = minSelections;
        MaxSelections = maxSelections;
        SortOrder = sortOrder;

        Touch();
    }

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

    public void DeactivateOption(ModifierOptionId optionId)
    {
        var option = GetOption(optionId);
        if (!option.IsActive)
        {
            return;
        }

        if (IsActive)
        {
            var remainingActive = _options.Count(o => o.IsActive && o.Id != optionId);
            if (remainingActive < MaxSelections && remainingActive > 0)
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
        var map = _options.ToDictionary(o => o.Id);
        foreach (var (optId, sortOrder) in orderings)
        {
            if (map.TryGetValue(optId, out var option))
            {
                option.UpdateSortOrder(sortOrder);
            }
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

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        ValidateOptionsInvariant();
        IsActive = true;
        Touch();
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Touch();
    }

    public void UpdateSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        if (SortOrder == sortOrder)
        {
            return;
        }

        SortOrder = sortOrder;
        Touch();
    }

    public void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    private ModifierOption GetOption(ModifierOptionId optionId)
    {
        return _options.FirstOrDefault(o => o.Id == optionId)
            ?? throw new DomainException($"ModifierOption '{optionId.Value}' does not belong to group '{Name}'.");
    }

    private static void ValidateSelectionBounds(int min, int max)
    {
        if (min < 0)
        {
            throw new DomainException("MinSelections cannot be negative.");
        }

        if (max < 0)
        {
            throw new DomainException("MaxSelections cannot be negative.");
        }

        if (min > max)
        {
            throw new DomainException($"MinSelections ({min}) cannot exceed MaxSelections ({max}).");
        }
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Modifier group name cannot be empty.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 100)
        {
            throw new DomainException("Modifier group name must be between 1 and 100 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Modifier group name cannot contain HTML or markup tags.");
        }

        return trimmed;
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
