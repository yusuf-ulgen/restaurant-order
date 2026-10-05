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
public partial class ModifierGroup
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

    public void UpdateDetails(string name, int minSelections, int maxSelections, int sortOrder, bool isAssigned = false)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        var validatedName = ValidateName(name);
        ValidateSelectionBounds(minSelections, maxSelections);

        var activeOptionsCount = _options.Count(o => o.IsActive);
        if (isAssigned)
        {
            if (activeOptionsCount < maxSelections)
            {
                throw new DomainException(
                    $"MaxSelections ({maxSelections}) cannot exceed total active options ({activeOptionsCount}) for assigned group '{validatedName}'.");
            }

            if (activeOptionsCount < minSelections)
            {
                throw new DomainException(
                    $"MinSelections ({minSelections}) cannot exceed total active options ({activeOptionsCount}) for assigned group '{validatedName}'.");
            }
        }
        else if (activeOptionsCount > 0)
        {
            if (maxSelections > activeOptionsCount)
            {
                throw new DomainException(
                    $"MaxSelections ({maxSelections}) cannot exceed total active options ({activeOptionsCount}) for group '{validatedName}'.");
            }

            if (minSelections > activeOptionsCount)
            {
                throw new DomainException(
                    $"MinSelections ({minSelections}) cannot exceed total active options ({activeOptionsCount}) for group '{validatedName}'.");
            }
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

}
