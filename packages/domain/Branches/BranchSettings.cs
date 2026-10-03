using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Operational and financial configuration aggregate root for a branch.
/// Encapsulates tax policy, service charge thresholds, order taking gates, and locale support.
/// </summary>
public class BranchSettings
{
    public BranchSettingsId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }

    public Timezone Timezone { get; private set; }
    public Currency Currency { get; private set; }
    public string DefaultLocale { get; private set; } = null!;
    public string SupportedLocalesJson { get; private set; } = null!;

    public bool PricesIncludeTax { get; private set; }
    public int DefaultTaxRateBps { get; private set; }
    public bool IsServiceChargeEnabled { get; private set; }
    public int ServiceChargeRateBps { get; private set; }
    public bool IsOrderTakingEnabled { get; private set; }

    public string? DisplayName { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }

    public Guid ConcurrencyToken { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private BranchSettings()
    {
    }

    public static BranchSettings Create(
        TenantId tenantId,
        BranchId branchId,
        string timezone = Timezone.DefaultId,
        string currency = Currency.DefaultCode,
        string defaultLocale = SupportedLocales.DefaultLocale,
        IEnumerable<string>? supportedLocales = null,
        bool pricesIncludeTax = true,
        int defaultTaxRateBps = 0,
        bool isServiceChargeEnabled = false,
        int serviceChargeRateBps = 0,
        bool isOrderTakingEnabled = true,
        string? displayName = null,
        string? phoneNumber = null,
        string? email = null,
        string? address = null,
        BranchSettingsId? id = null)
    {
        var settingsId = id ?? BranchSettingsId.New();
        var locales = supportedLocales?.ToList() ?? new List<string> { defaultLocale };

        SupportedLocales.Validate(defaultLocale, locales);
        var taxRate = BasisPointsRate.FromTaxRateBps(defaultTaxRateBps);
        var serviceRate = BasisPointsRate.FromServiceChargeRateBps(serviceChargeRateBps, isServiceChargeEnabled);

        var settings = new BranchSettings
        {
            Id = settingsId,
            TenantId = tenantId,
            BranchId = branchId,
            Timezone = new Timezone(timezone),
            Currency = new Currency(currency),
            DefaultLocale = defaultLocale.Trim(),
            SupportedLocalesJson = SupportedLocales.Serialize(locales),
            PricesIncludeTax = pricesIncludeTax,
            DefaultTaxRateBps = taxRate.Value,
            IsServiceChargeEnabled = isServiceChargeEnabled,
            ServiceChargeRateBps = serviceRate.Value,
            IsOrderTakingEnabled = isOrderTakingEnabled,
            DisplayName = displayName?.Trim(),
            PhoneNumber = phoneNumber?.Trim(),
            Email = email?.Trim(),
            Address = address?.Trim(),
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        };

        return settings;
    }

    public void UpdateDetails(
        string timezone,
        string currency,
        string defaultLocale,
        IEnumerable<string> supportedLocales,
        bool pricesIncludeTax,
        int defaultTaxRateBps,
        bool isServiceChargeEnabled,
        int serviceChargeRateBps,
        bool isOrderTakingEnabled,
        string? displayName = null,
        string? phoneNumber = null,
        string? email = null,
        string? address = null)
    {
        SupportedLocales.Validate(defaultLocale, supportedLocales);
        var taxRate = BasisPointsRate.FromTaxRateBps(defaultTaxRateBps);
        var serviceRate = BasisPointsRate.FromServiceChargeRateBps(serviceChargeRateBps, isServiceChargeEnabled);

        Timezone = new Timezone(timezone);
        Currency = new Currency(currency);
        DefaultLocale = defaultLocale.Trim();
        SupportedLocalesJson = SupportedLocales.Serialize(supportedLocales);
        PricesIncludeTax = pricesIncludeTax;
        DefaultTaxRateBps = taxRate.Value;
        IsServiceChargeEnabled = isServiceChargeEnabled;
        ServiceChargeRateBps = serviceRate.Value;
        IsOrderTakingEnabled = isOrderTakingEnabled;
        DisplayName = displayName?.Trim();
        PhoneNumber = phoneNumber?.Trim();
        Email = email?.Trim();
        Address = address?.Trim();

        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }
}
