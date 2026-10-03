using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Immutable value object representing a financial percentage rate expressed in basis points (1 bp = 0.01%).
/// Completely avoids floating-point inaccuracies for tax and service charges.
/// </summary>
public readonly record struct BasisPointsRate
{
    public const int MaxTaxRateBps = 10000; // 100.00%
    public const int MaxServiceChargeRateBps = 5000; // 50.00%

    public int Value { get; }

    public decimal AsDecimal => Value / 10000m;
    public decimal AsPercentage => Value / 100m;

    private BasisPointsRate(int value)
    {
        Value = value;
    }

    public static BasisPointsRate FromTaxRateBps(int bps)
    {
        if (bps < 0)
        {
            throw new DomainException("Tax rate cannot be negative.");
        }

        if (bps > MaxTaxRateBps)
        {
            throw new DomainException($"Tax rate cannot exceed {MaxTaxRateBps / 100}% ({MaxTaxRateBps} bps).");
        }

        return new BasisPointsRate(bps);
    }

    public static BasisPointsRate FromServiceChargeRateBps(int bps, bool isServiceChargeEnabled)
    {
        if (!isServiceChargeEnabled && bps != 0)
        {
            throw new DomainException("Service charge rate must be 0 when service charge is disabled.");
        }

        if (bps < 0)
        {
            throw new DomainException("Service charge rate cannot be negative.");
        }

        if (bps > MaxServiceChargeRateBps)
        {
            throw new DomainException($"Service charge rate cannot exceed {MaxServiceChargeRateBps / 100}% ({MaxServiceChargeRateBps} bps).");
        }

        return new BasisPointsRate(bps);
    }

    public static BasisPointsRate Zero => new(0);

    public override string ToString() => $"{AsPercentage.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}%";

    public static implicit operator int(BasisPointsRate rate) => rate.Value;
}
