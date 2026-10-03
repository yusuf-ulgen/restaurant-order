using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Operational preparation station within a branch (e.g. Kitchen, Bar, Bakery).
/// Order items are routed to stations for ticket preparation and KDS display.
/// </summary>
public class PreparationStation
{
    private static readonly Regex CodeRegex = new(@"^[a-z0-9]+([-_][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    public PreparationStationId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public string Code { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public PreparationStationType StationType { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private PreparationStation()
    {
    }

    public static PreparationStation Create(
        TenantId tenantId,
        BranchId branchId,
        string code,
        string displayName,
        PreparationStationType stationType,
        int sortOrder = 0,
        PreparationStationId? id = null)
    {
        var validatedCode = ValidateCode(code);
        var validatedDisplayName = ValidateDisplayName(displayName);

        if (sortOrder < 0)
        {
            throw new DomainException("SortOrder cannot be negative.");
        }

        var stationId = id ?? PreparationStationId.New();

        return new PreparationStation
        {
            Id = stationId,
            TenantId = tenantId,
            BranchId = branchId,
            Code = validatedCode,
            DisplayName = validatedDisplayName,
            StationType = stationType,
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
    }

    public void Update(string displayName, PreparationStationType stationType)
    {
        DisplayName = ValidateDisplayName(displayName);
        StationType = stationType;
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


    private static string ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("Preparation station display name is required.");
        }

        var trimmed = displayName.Trim();
        if (trimmed.Length is < 2 or > 100)
        {
            throw new DomainException("Preparation station display name must be between 2 and 100 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Preparation station display name cannot contain HTML or script markup.");
        }

        return trimmed;
    }

    private static string ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("Preparation station code is required.");
        }

        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.Length is < 2 or > 50)
        {
            throw new DomainException("Preparation station code must be between 2 and 50 characters.");
        }

        if (!CodeRegex.IsMatch(normalized))
        {
            throw new DomainException(
                "Preparation station code must contain only lowercase letters, digits, and non-consecutive hyphens or underscores.");
        }

        return normalized;
    }
}
