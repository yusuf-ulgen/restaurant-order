using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Physical restaurant branch entity belonging to a Tenant and Brand.
/// Manages operational lifecycle, timezone, and currency conventions.
/// </summary>
public class Branch
{
    public BranchId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BrandId BrandId { get; private set; }
    public string Name { get; private set; } = null!;
    public Slug Slug { get; private set; }
    public Timezone Timezone { get; private set; }
    public Currency Currency { get; private set; }
    public BranchStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private Branch()
    {
    }

    private Branch(
        BranchId id,
        TenantId tenantId,
        BrandId brandId,
        string name,
        Slug slug,
        Timezone timezone,
        Currency currency,
        DateTime createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        BrandId = brandId;
        SetName(name);
        Slug = slug;
        Timezone = timezone;
        Currency = currency;
        Status = BranchStatus.Active;
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public static Branch Create(
        TenantId tenantId,
        Brand brand,
        string name,
        string slug,
        string timezone = Timezone.DefaultId,
        string currency = Currency.DefaultCode,
        BranchId? id = null)
    {
        if (brand.TenantId != tenantId)
        {
            throw new DomainException(
                $"Tenant mismatch: Brand '{brand.Name}' belongs to tenant '{brand.TenantId}', but branch was requested for tenant '{tenantId}'.");
        }

        var branchId = id ?? BranchId.New();
        var slugVo = new Slug(slug);
        var timezoneVo = new Timezone(timezone);
        var currencyVo = new Currency(currency);

        return new Branch(
            branchId,
            tenantId,
            brand.Id,
            name,
            slugVo,
            timezoneVo,
            currencyVo,
            DateTime.UtcNow);
    }

    public static Branch Create(
        TenantId tenantId,
        BrandId brandId,
        string name,
        string slug,
        string timezone = Timezone.DefaultId,
        string currency = Currency.DefaultCode,
        BranchId? id = null)
    {
        var branchId = id ?? BranchId.New();
        var slugVo = new Slug(slug);
        var timezoneVo = new Timezone(timezone);
        var currencyVo = new Currency(currency);

        return new Branch(
            branchId,
            tenantId,
            brandId,
            name,
            slugVo,
            timezoneVo,
            currencyVo,
            DateTime.UtcNow);
    }

    public void UpdateDetails(string newName, string newTimezone, string newCurrency)
    {
        EnsureNotClosed();
        SetName(newName);
        Timezone = new Timezone(newTimezone);
        Currency = new Currency(newCurrency);
        Touch();
    }

    public void Suspend(string? reason = null)
    {
        if (Status == BranchStatus.Closed)
        {
            throw new DomainException("Cannot suspend a closed branch. Closed is a terminal state.");
        }

        if (Status == BranchStatus.Suspended)
        {
            return;
        }

        Status = BranchStatus.Suspended;
        Touch();
    }

    public void Activate()
    {
        if (Status == BranchStatus.Closed)
        {
            throw new DomainException("Cannot activate a permanently closed branch.");
        }

        if (Status == BranchStatus.Active)
        {
            return;
        }

        Status = BranchStatus.Active;
        Touch();
    }

    public void Close(string? reason = null)
    {
        if (Status == BranchStatus.Closed)
        {
            return;
        }

        Status = BranchStatus.Closed;
        Touch();
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Branch name cannot be null, empty, or whitespace.");
        }

        Name = name.Trim();
    }

    private void EnsureNotClosed()
    {
        if (Status == BranchStatus.Closed)
        {
            throw new DomainException("Modifications are not allowed on a closed branch.");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }
}
