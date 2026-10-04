using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Branch-scoped Menu aggregate root.
/// Represents a curated collection of categories and items available for guest dining and operations.
/// </summary>
public class Menu
{
    private static readonly Regex SlugRegex = new(@"^[a-z0-9]+([-_][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    public MenuId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public MenuStatus Status { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private Menu()
    {
    }

    public static Menu Create(
        TenantId tenantId,
        BranchId branchId,
        string name,
        string slug,
        string? description = null,
        int sortOrder = 0,
        MenuStatus status = MenuStatus.Draft,
        MenuId? id = null)
    {
        var validatedName = ValidateName(name);
        var validatedSlug = ValidateSlug(slug);
        var validatedDescription = ValidateDescription(description);

        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        if (status == MenuStatus.Archived)
        {
            throw new DomainException("Cannot create a menu directly in Archived status.");
        }

        var menuId = id ?? MenuId.New();

        return new Menu
        {
            Id = menuId,
            TenantId = tenantId,
            BranchId = branchId,
            Name = validatedName,
            Slug = validatedSlug,
            Description = validatedDescription,
            Status = status,
            SortOrder = sortOrder,
            CreatedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
    }

    public void UpdateDetails(string name, string? description, int sortOrder)
    {
        EnsureNotArchived();

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
        EnsureNotArchived();

        if (Status == MenuStatus.Active)
        {
            return;
        }

        Status = MenuStatus.Active;
        Touch();
    }

    public void Archive()
    {
        if (Status == MenuStatus.Archived)
        {
            return;
        }

        Status = MenuStatus.Archived;
        Touch();
    }

    public void UpdateSortOrder(int sortOrder)
    {
        EnsureNotArchived();

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

    public void EnsureNotArchived()
    {
        if (Status == MenuStatus.Archived)
        {
            throw new DomainException("Cannot modify an archived menu. Archived is a terminal state.");
        }
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Menu name cannot be empty.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 100)
        {
            throw new DomainException("Menu name must be between 1 and 100 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Menu name cannot contain HTML or markup tags.");
        }

        return trimmed;
    }

    private static string ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainException("Menu slug cannot be empty.");
        }

        var normalized = slug.Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 50)
        {
            throw new DomainException("Menu slug must be between 1 and 50 characters.");
        }

        if (!SlugRegex.IsMatch(normalized))
        {
            throw new DomainException($"Invalid menu slug format '{slug}'. Use lowercase letters, digits, and hyphens/underscores.");
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
            throw new DomainException("Menu description cannot exceed 500 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Menu description cannot contain HTML or markup tags.");
        }

        return trimmed;
    }
}
