using System.Security.Cryptography;
using System.Text;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Orders.Pricing;

/// <summary>
/// Authoritative immutable pricing policy for a branch.
/// Unifies runtime menu display and backend order pricing calculations under identical financial rules,
/// basis-point tax/service charge fallbacks, and a deterministic policy fingerprint.
/// </summary>
public sealed record EffectiveBranchPricingPolicy
{
    public string Currency { get; init; }
    public bool PricesIncludeTax { get; init; }
    public int DefaultTaxRateBps { get; init; }
    public bool IsServiceChargeEnabled { get; init; }
    public int ServiceChargeRateBps { get; init; }
    public bool IsOrderTakingEnabled { get; init; }
    public string PricingPolicyFingerprint { get; init; }

    private EffectiveBranchPricingPolicy(
        string currency,
        bool pricesIncludeTax,
        int defaultTaxRateBps,
        bool isServiceChargeEnabled,
        int serviceChargeRateBps,
        bool isOrderTakingEnabled,
        string pricingPolicyFingerprint)
    {
        Currency = currency;
        PricesIncludeTax = pricesIncludeTax;
        DefaultTaxRateBps = defaultTaxRateBps;
        IsServiceChargeEnabled = isServiceChargeEnabled;
        ServiceChargeRateBps = serviceChargeRateBps;
        IsOrderTakingEnabled = isOrderTakingEnabled;
        PricingPolicyFingerprint = pricingPolicyFingerprint;
    }

    public static EffectiveBranchPricingPolicy Create(Branch branch, BranchSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return Create(branch.Currency.Code, settings);
    }

    public static EffectiveBranchPricingPolicy Create(string currency, BranchSettings? settings)
    {
        var effectiveCurrency = !string.IsNullOrWhiteSpace(currency)
            ? currency.Trim().ToUpperInvariant()
            : Domain.Common.Currency.DefaultCode;

        bool pricesIncludeTax;
        int defaultTaxRateBps;
        bool isServiceChargeEnabled;
        int serviceChargeRateBps;
        bool isOrderTakingEnabled;

        if (settings != null)
        {
            if (!string.IsNullOrWhiteSpace(settings.Currency.Code))
            {
                effectiveCurrency = settings.Currency.Code.ToUpperInvariant();
            }

            pricesIncludeTax = settings.PricesIncludeTax;
            defaultTaxRateBps = settings.DefaultTaxRateBps;
            isServiceChargeEnabled = settings.IsServiceChargeEnabled;
            serviceChargeRateBps = settings.ServiceChargeRateBps;
            isOrderTakingEnabled = settings.IsOrderTakingEnabled;
        }
        else
        {
            // Fallback policy matching authoritative order pricing engine defaults
            pricesIncludeTax = true;
            defaultTaxRateBps = BasisPointsRate.DefaultTaxRateBps; // 1000 bps (10.00%)
            isServiceChargeEnabled = false;
            serviceChargeRateBps = 0;
            isOrderTakingEnabled = true;
        }

        var fingerprint = ComputeFingerprint(
            effectiveCurrency,
            pricesIncludeTax,
            defaultTaxRateBps,
            isServiceChargeEnabled,
            serviceChargeRateBps);

        return new EffectiveBranchPricingPolicy(
            effectiveCurrency,
            pricesIncludeTax,
            defaultTaxRateBps,
            isServiceChargeEnabled,
            serviceChargeRateBps,
            isOrderTakingEnabled,
            fingerprint);
    }

    public static string ComputeFingerprint(
        string currency,
        bool pricesIncludeTax,
        int defaultTaxRateBps,
        bool isServiceChargeEnabled,
        int serviceChargeRateBps)
    {
        var canonical = $"{currency.Trim().ToUpperInvariant()}|taxInc:{(pricesIncludeTax ? "1" : "0")}|taxBps:{defaultTaxRateBps}|scEnabled:{(isServiceChargeEnabled ? "1" : "0")}|scBps:{serviceChargeRateBps}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
