using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// MenuCategory entity grouping related menu items (e.g. Starters, Main Courses, Desserts).
/// Scoped to Tenant, Branch, and parent Menu.
/// </summary>
public class MenuCategory
{
    private static readonly Regex SlugRegex = new(@"^[a-z0-9]+([-_][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    public MenuCategoryId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public MenuId MenuId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private MenuCategory()
    {
    }

    public static MenuCategory Create(
        TenantId tenantId,
        BranchId branchId,
        MenuId menuId,
        string name,
        string slug,
        string? description = null,
        int sortOrder = 0,
        bool isActive = true,
        MenuCategoryId? id = null)
    {
        var validatedName = ValidateName(name);
        var validatedSlug = ValidateSlug(slug);
        var validatedDescription = ValidateDescription(description);

        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        var categoryId = id ?? MenuCategoryId.New();

        return new MenuCategory
        {
            Id = categoryId,
            TenantId = tenantId,
            BranchId = branchId,
            MenuId = menuId,
            Name = validatedName,
            Slug = validatedSlug,
            Description = validatedDescription,
            SortOrder = sortOrder,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
    }

    public void UpdateDetails(string name, string? description, int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        Name = ValidateName(name);
        Description = ValidateDescription(description);
        SortOrder = sortOrder;
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
            throw new DomainException("Category name cannot be empty.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 100)
        {
            throw new DomainException("Category name must be between 1 and 100 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Category name cannot contain HTML or markup tags.");
        }

        return trimmed;
    }

    private static string ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainException("Category slug cannot be empty.");
        }

        var normalized = slug.Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 50)
        {
            throw new DomainException("Category slug must be between 1 and 50 characters.");
        }

        if (!SlugRegex.IsMatch(normalized))
        {
            throw new DomainException($"Invalid category slug format '{slug}'. Use lowercase letters, digits, and hyphens/underscores.");
        }

        return normalized;
    }

    private static string? ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();
        if (trimmed.Length > 500)
        {
            throw new DomainException("Category description cannot exceed 500 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Category description cannot contain HTML or markup tags.");
        }

        return trimmed;
    }
}
