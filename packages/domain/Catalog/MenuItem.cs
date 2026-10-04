using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// MenuItem aggregate root representing a catalog item (food, drink, or product).
/// Can be sold directly via its BasePriceMinorUnits or through configurable ItemVariants.
/// </summary>
public class MenuItem
{
    private static readonly Regex SlugRegex = new(@"^[a-z0-9]+([-_][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    private readonly List<ItemVariant> _variants = new();
    private readonly List<MenuItemModifierGroupAssignment> _modifierGroupAssignments = new();
    private HashSet<DietaryTag> _dietaryTags = new();
    private HashSet<AllergenTag> _allergenTags = new();

    public MenuItemId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public MenuId MenuId { get; private set; }
    public MenuCategoryId CategoryId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? ShortDescription { get; private set; }
    public string? FullDescription { get; private set; }
    public string? ImageUrl { get; private set; }
    public PriceAmount BasePriceMinorUnits { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public SpicyLevel SpicyLevel { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public IReadOnlyList<ItemVariant> Variants => _variants.AsReadOnly();
    public IReadOnlyList<MenuItemModifierGroupAssignment> ModifierGroupAssignments => _modifierGroupAssignments.AsReadOnly();
    public IReadOnlySet<DietaryTag> DietaryTags => _dietaryTags;
    public IReadOnlySet<AllergenTag> AllergenTags => _allergenTags;

    // Parameterless constructor for EF Core persistence materialization
    private MenuItem()
    {
    }

    public static MenuItem Create(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        MenuCategoryId categoryId,
        string name,
        string slug,
        PriceAmount basePrice,
        string? shortDescription = null,
        string? fullDescription = null,
        string? imageUrl = null,
        int sortOrder = 0,
        bool isActive = true,
        MenuItemId? id = null)
    {
        var validatedName = ValidateName(name);
        var validatedSlug = ValidateSlug(slug);
        var validatedShortDesc = ValidateShortDescription(shortDescription);
        var validatedFullDesc = ValidateFullDescription(fullDescription);
        var validatedImageUrl = ValidateImageUrl(imageUrl);

        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        var itemId = id ?? MenuItemId.New();

        return new MenuItem
        {
            Id = itemId,
            TenantId = tenantId,
            BranchId = branchId,
            MenuId = menuId,
            CategoryId = categoryId,
            Name = validatedName,
            Slug = validatedSlug,
            ShortDescription = validatedShortDesc,
            FullDescription = validatedFullDesc,
            ImageUrl = validatedImageUrl,
            BasePriceMinorUnits = basePrice,
            SortOrder = sortOrder,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
    }

    public void UpdateDetails(
        string name,
        MenuCategoryId categoryId,
        string? shortDescription,
        string? fullDescription,
        string? imageUrl,
        int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        Name = ValidateName(name);
        CategoryId = categoryId;
        ShortDescription = ValidateShortDescription(shortDescription);
        FullDescription = ValidateFullDescription(fullDescription);
        ImageUrl = ValidateImageUrl(imageUrl);
        SortOrder = sortOrder;
        Touch();
    }

    public void UpdateBasePrice(PriceAmount newPrice)
    {
        if (BasePriceMinorUnits == newPrice)
        {
            return;
        }

        BasePriceMinorUnits = newPrice;
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

    public ItemVariant AddVariant(
        string name,
        string code,
        PriceAmount absolutePrice,
        int sortOrder = 0,
        bool isDefault = false,
        bool isActive = true)
    {
        var variant = ItemVariant.Create(
            tenantId: TenantId,
            branchId: BranchId,
            menuId: MenuId,
            menuItemId: Id,
            name: name,
            code: code,
            absolutePrice: absolutePrice,
            sortOrder: sortOrder,
            isDefault: isDefault,
            isActive: isActive);

        AddVariant(variant);
        return variant;
    }

    public void AddVariant(ItemVariant variant)
    {
        if (variant.IsDefault && variant.IsActive && _variants.Any(v => v.IsDefault && v.IsActive && v.Id != variant.Id))
        {
            throw new DomainException("A menu item can have at most one active default variant.");
        }

        _variants.Add(variant);
        Touch();
    }

    public void SetDefaultVariant(ItemVariantId variantId)
    {
        var target = _variants.FirstOrDefault(v => v.Id == variantId)
            ?? throw new DomainException($"Variant '{variantId.Value}' does not belong to this item.");

        foreach (var v in _variants)
        {
            v.SetDefault(v.Id == variantId);
        }

        Touch();
    }

    public void EnsureSingleActiveDefaultVariant()
    {
        var activeDefaults = _variants.Count(v => v.IsActive && v.IsDefault);
        if (activeDefaults > 1)
        {
            throw new DomainException("A menu item can have at most one active default variant.");
        }
    }

    public void UpdateMetadata(
        IEnumerable<DietaryTag> dietaryTags,
        IEnumerable<AllergenTag> allergenTags,
        SpicyLevel spicyLevel)
    {
        var distinctDietary = new HashSet<DietaryTag>(dietaryTags);
        var distinctAllergens = new HashSet<AllergenTag>(allergenTags);

        ValidateDietaryAndAllergenConsistency(distinctDietary, distinctAllergens);

        _dietaryTags.Clear();
        foreach (var tag in distinctDietary)
        {
            _dietaryTags.Add(tag);
        }

        _allergenTags.Clear();
        foreach (var allergen in distinctAllergens)
        {
            _allergenTags.Add(allergen);
        }

        SpicyLevel = spicyLevel;
        Touch();
    }

    public void AssignModifierGroup(ModifierGroup group, int sortOrder = 0)
    {
        if (group.TenantId != TenantId)
        {
            throw new DomainException($"Cannot assign modifier group from tenant '{group.TenantId.Value}' to menu item of tenant '{TenantId.Value}'.");
        }

        if (group.BranchId != BranchId)
        {
            throw new DomainException($"Cannot assign modifier group from branch '{group.BranchId.Value}' to menu item of branch '{BranchId.Value}'.");
        }

        group.ValidateForAssignment();
        AssignModifierGroup(group.Id, sortOrder);
    }

    public void AssignModifierGroup(ModifierGroupId modifierGroupId, int sortOrder = 0)
    {
        if (_modifierGroupAssignments.Any(a => a.ModifierGroupId == modifierGroupId))
        {
            throw new DomainException($"ModifierGroup '{modifierGroupId.Value}' is already assigned to this menu item.");
        }

        var assignment = MenuItemModifierGroupAssignment.Create(
            TenantId,
            BranchId,
            MenuId,
            Id,
            modifierGroupId,
            sortOrder);

        _modifierGroupAssignments.Add(assignment);
        Touch();
    }

    public void RemoveModifierGroup(ModifierGroupId modifierGroupId)
    {
        var existing = _modifierGroupAssignments.FirstOrDefault(a => a.ModifierGroupId == modifierGroupId);
        if (existing is null)
        {
            throw new DomainException($"ModifierGroup '{modifierGroupId.Value}' is not assigned to this menu item.");
        }

        _modifierGroupAssignments.Remove(existing);
        Touch();
    }

    public void ReorderModifierGroups(IReadOnlyList<(ModifierGroupId GroupId, int SortOrder)> orderings)
    {
        var map = _modifierGroupAssignments.ToDictionary(a => a.ModifierGroupId);
        foreach (var (groupId, sortOrder) in orderings)
        {
            if (map.TryGetValue(groupId, out var assignment))
            {
                assignment.UpdateSortOrder(sortOrder);
            }
        }
        Touch();
    }

    public static void ValidateDietaryAndAllergenConsistency(ISet<DietaryTag> dietaryTags, ISet<AllergenTag> allergenTags) =>
        DietaryAndAllergenValidator.ValidateConsistency(dietaryTags, allergenTags);

    public void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Item name cannot be empty.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 100)
        {
            throw new DomainException("Item name must be between 1 and 100 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Item name cannot contain HTML or markup tags.");
        }

        return trimmed;
    }

    private static string ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainException("Item slug cannot be empty.");
        }

        var normalized = slug.Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 50)
        {
            throw new DomainException("Item slug must be between 1 and 50 characters.");
        }

        if (!SlugRegex.IsMatch(normalized))
        {
            throw new DomainException($"Invalid item slug format '{slug}'. Use lowercase letters, digits, and hyphens/underscores.");
        }

        return normalized;
    }

    private static string? ValidateShortDescription(string? shortDesc)
    {
        if (string.IsNullOrWhiteSpace(shortDesc))
        {
            return null;
        }

        var trimmed = shortDesc.Trim();
        if (trimmed.Length > 200)
        {
            throw new DomainException("Short description cannot exceed 200 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Short description cannot contain HTML or markup tags.");
        }

        return trimmed;
    }

    private static string? ValidateFullDescription(string? fullDesc)
    {
        if (string.IsNullOrWhiteSpace(fullDesc))
        {
            return null;
        }

        var trimmed = fullDesc.Trim();
        if (trimmed.Length > 2000)
        {
            throw new DomainException("Full description cannot exceed 2000 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Full description cannot contain HTML or markup tags.");
        }

        return trimmed;
    }

    public static string? ValidateImageUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var trimmed = imageUrl.Trim();
        if (trimmed.Length > 500)
        {
            throw new DomainException("ImageUrl cannot exceed 500 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("ImageUrl cannot contain HTML or markup tags.");
        }

        var lower = trimmed.ToLowerInvariant();
        if (lower.StartsWith("javascript:") || lower.StartsWith("data:") || lower.StartsWith("vbscript:"))
        {
            throw new DomainException("ImageUrl cannot contain javascript or data schemes.");
        }

        if (trimmed.StartsWith("//"))
        {
            throw new DomainException("Protocol-relative URLs are not permitted.");
        }

        if (trimmed.StartsWith("/"))
        {
            return trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        throw new DomainException("ImageUrl must be a secure relative path (starting with '/') or an HTTPS URL ('https://').");
    }
}
