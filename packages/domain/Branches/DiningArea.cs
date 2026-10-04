using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Physical dining area within a restaurant branch (e.g. Indoor, Terrace, Garden, Bar Area).
/// Encapsulates naming, slug code, area type, ordering, and active lifecycle.
/// </summary>
public class DiningArea
{
    private static readonly Regex SlugRegex = new(@"^[a-z0-9]+([-_][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    public DiningAreaId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public DiningAreaType AreaType { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private DiningArea()
    {
    }

    public static DiningArea Create(
        TenantId tenantId,
        BranchId branchId,
        string name,
        string code,
        DiningAreaType areaType,
        int sortOrder = 0,
        DiningAreaId? id = null)
    {
        var validatedName = ValidateName(name);
        var validatedCode = ValidateCode(code);

        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        var areaId = id ?? DiningAreaId.New();

        return new DiningArea
        {
            Id = areaId,
            TenantId = tenantId,
            BranchId = branchId,
            Name = validatedName,
            Code = validatedCode,
            AreaType = areaType,
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
    }

    public void Update(string name, DiningAreaType areaType)
    {
        Name = ValidateName(name);
        AreaType = areaType;
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void SetSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        if (SortOrder != sortOrder)
        {
            SortOrder = sortOrder;
            UpdatedAtUtc = DateTime.UtcNow;
            ConcurrencyToken = Guid.NewGuid();
        }
    }

    public void Activate()
    {
        if (!IsActive)
        {
            IsActive = true;
            UpdatedAtUtc = DateTime.UtcNow;
            ConcurrencyToken = Guid.NewGuid();
        }
    }

    public void Deactivate()
    {
        if (IsActive)
        {
            IsActive = false;
            UpdatedAtUtc = DateTime.UtcNow;
            ConcurrencyToken = Guid.NewGuid();
        }
    }


    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Dining area name is required.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length is < 2 or > 100)
        {
            throw new DomainException("Dining area name must be between 2 and 100 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Dining area name cannot contain HTML or script markup.");
        }

        return trimmed;
    }

    private static string ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("Dining area code is required.");
        }

        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.Length is < 2 or > 50)
        {
            throw new DomainException("Dining area code must be between 2 and 50 characters.");
        }

        if (!SlugRegex.IsMatch(normalized))
        {
            throw new DomainException(
                "Dining area code must contain only lowercase letters, digits, and non-consecutive hyphens or underscores.");
        }

        return normalized;
    }
}
