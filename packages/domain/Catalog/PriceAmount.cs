using System.Globalization;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Immutable value object representing a monetary price stored in integer minor units (e.g. kuruş, cents).
/// Completely eliminates floating-point inaccuracies and culture/locale dependencies.
/// </summary>
public readonly record struct PriceAmount : IComparable<PriceAmount>
{
    /// <summary>
    /// Reasonable maximum ceiling for single item/variant price: 1,000,000,000 minor units (e.g. 10,000,000.00 currency units).
    /// </summary>
    public const long MaxMinorUnits = 1_000_000_000L;

    public long MinorUnits { get; }

    private PriceAmount(long minorUnits)
    {
        if (minorUnits < 0)
        {
            throw new DomainException("Price amount cannot be negative.");
        }

        if (minorUnits > MaxMinorUnits)
        {
            throw new DomainException($"Price amount cannot exceed {MaxMinorUnits} minor units.");
        }

        MinorUnits = minorUnits;
    }

    public static PriceAmount FromMinorUnits(long minorUnits) => new(minorUnits);

    public static PriceAmount Zero => new(0);

    public int CompareTo(PriceAmount other) => MinorUnits.CompareTo(other.MinorUnits);

    public override string ToString() => MinorUnits.ToString(CultureInfo.InvariantCulture);

    public static implicit operator long(PriceAmount price) => price.MinorUnits;

    public static explicit operator PriceAmount(long minorUnits) => FromMinorUnits(minorUnits);

    public static bool operator <(PriceAmount left, PriceAmount right) => left.MinorUnits < right.MinorUnits;

    public static bool operator >(PriceAmount left, PriceAmount right) => left.MinorUnits > right.MinorUnits;

    public static bool operator <=(PriceAmount left, PriceAmount right) => left.MinorUnits <= right.MinorUnits;

    public static bool operator >=(PriceAmount left, PriceAmount right) => left.MinorUnits >= right.MinorUnits;

    public static PriceAmount operator +(PriceAmount left, PriceAmount right) =>
        FromMinorUnits(left.MinorUnits + right.MinorUnits);

    public static PriceAmount operator -(PriceAmount left, PriceAmount right)
    {
        if (left.MinorUnits < right.MinorUnits)
        {
            throw new DomainException("Price subtraction would result in a negative price amount.");
        }

        return FromMinorUnits(left.MinorUnits - right.MinorUnits);
    }

    public static PriceAmount operator *(PriceAmount price, int multiplier)
    {
        if (multiplier < 0)
        {
            throw new DomainException("Multiplier cannot be negative.");
        }

        return FromMinorUnits(price.MinorUnits * multiplier);
    }
}
