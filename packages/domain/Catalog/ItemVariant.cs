using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// ItemVariant entity representing a specific sellable configuration of a MenuItem (e.g., portion, size).
/// Carries an explicit, absolute price in integer minor units.
/// </summary>
public class ItemVariant
{
    private static readonly Regex CodeRegex = new(@"^[A-Za-z0-9]+([-_][A-Za-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    public ItemVariantId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public MenuId MenuId { get; private set; }
    public MenuItemId MenuItemId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public PriceAmount AbsolutePriceMinorUnits { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private ItemVariant()
    {
    }

    public static ItemVariant Create(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuItemId menuItemId,
        string name,
        string code,
        PriceAmount absolutePrice,
        int sortOrder = 0,
        bool isDefault = false,
        bool isActive = true,
        ItemVariantId? id = null)
    {
        var validatedName = ValidateName(name);
        var validatedCode = ValidateCode(code);

        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        var variantId = id ?? ItemVariantId.New();

        return new ItemVariant
        {
            Id = variantId,
            TenantId = tenantId,
            BranchId = branchId,
            MenuId = menuId,
            MenuItemId = menuItemId,
            Name = validatedName,
            Code = validatedCode,
            AbsolutePriceMinorUnits = absolutePrice,
            SortOrder = sortOrder,
            IsDefault = isDefault,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
    }

    public void UpdateDetails(string name, string code, PriceAmount absolutePrice, int sortOrder, bool isDefault)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        Name = ValidateName(name);
        Code = ValidateCode(code);
        AbsolutePriceMinorUnits = absolutePrice;
        SortOrder = sortOrder;
        IsDefault = isDefault;
        Touch();
    }

    public void UpdateDetails(string name, string code, int sortOrder, bool isDefault)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        Name = ValidateName(name);
        Code = ValidateCode(code);
        SortOrder = sortOrder;
        IsDefault = isDefault;
        Touch();
    }

    public void UpdatePrice(PriceAmount newPrice)
    {
        if (AbsolutePriceMinorUnits == newPrice)
        {
            return;
        }

        AbsolutePriceMinorUnits = newPrice;
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
            throw new DomainException("Variant name cannot be empty.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 100)
        {
            throw new DomainException("Variant name must be between 1 and 100 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Variant name cannot contain HTML or markup tags.");
        }

        return trimmed;
    }

    private static string ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("Variant code cannot be empty.");
        }

        var trimmed = code.Trim().ToUpperInvariant();
        if (trimmed.Length is < 1 or > 50)
        {
            throw new DomainException("Variant code must be between 1 and 50 characters.");
        }

        if (!CodeRegex.IsMatch(trimmed))
        {
            throw new DomainException($"Invalid variant code format '{code}'. Use alphanumeric characters, hyphens, and underscores.");
        }

        return trimmed;
    }
}
