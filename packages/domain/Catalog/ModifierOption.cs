using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// ModifierOption entity representing a single customizable choice within a ModifierGroup.
/// Has a non-negative price delta stored in integer minor units.
/// </summary>
public class ModifierOption
{
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    public ModifierOptionId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public ModifierGroupId ModifierGroupId { get; private set; }
    public string Name { get; private set; } = null!;
    public PriceAmount PriceDeltaMinorUnits { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    private ModifierOption()
    {
    }

    public static ModifierOption Create(
        TenantId tenantId,
        BranchId branchId,
        ModifierGroupId modifierGroupId,
        string name,
        PriceAmount priceDelta,
        int sortOrder = 0,
        bool isDefault = false,
        bool isActive = true,
        ModifierOptionId? id = null)
    {
        var validatedName = ValidateName(name);

        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        return new ModifierOption
        {
            Id = id ?? ModifierOptionId.New(),
            TenantId = tenantId,
            BranchId = branchId,
            ModifierGroupId = modifierGroupId,
            Name = validatedName,
            PriceDeltaMinorUnits = priceDelta,
            SortOrder = sortOrder,
            IsDefault = isDefault,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
    }

    public void UpdateDetails(string name, int sortOrder, bool isDefault)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        Name = ValidateName(name);
        SortOrder = sortOrder;
        IsDefault = isDefault;
        Touch();
    }

    public void UpdatePriceDelta(PriceAmount newPriceDelta)
    {
        if (PriceDeltaMinorUnits == newPriceDelta)
        {
            return;
        }

        PriceDeltaMinorUnits = newPriceDelta;
        Touch();
    }

    public void SetDefault(bool isDefault)
    {
        if (IsDefault == isDefault)
        {
            return;
        }

        IsDefault = isDefault;
        Touch();
    }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

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

    private static string ValidateName(string name)
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
