using System.Text.RegularExpressions;

namespace RestaurantOrder.Domain.Common;

/// <summary>
/// Immutable value object representing an ISO 4217 three-letter currency code.
/// Default currency is TRY, but not hardcoded as the sole supported currency.
/// </summary>
public readonly record struct Currency
{
    private static readonly Regex CurrencyFormatRegex = new(
        @"^[A-Z]{3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const string DefaultCode = "TRY";

    private static readonly HashSet<string> RecognizedIsoCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRY", "USD", "EUR", "GBP", "AED", "SAR", "CHF", "JPY",
        "CAD", "AUD", "SEK", "NOK", "DKK", "QAR", "KWD", "BHD",
        "OMR", "EGP", "AZN", "GEL", "KZT", "PLN", "CZK", "HUF",
        "BRL", "INR", "SGD", "NZD", "MXN", "ZAR"
    };

    public string Code { get; }

    public Currency(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("Currency code cannot be null, empty, or whitespace.");
        }

        var trimmed = code.Trim();

        if (!CurrencyFormatRegex.IsMatch(trimmed))
        {
            throw new DomainException(
                $"Currency code '{trimmed}' is invalid. It must be a 3-letter uppercase ISO 4217 code.");
        }

        if (!RecognizedIsoCodes.Contains(trimmed))
        {
            throw new DomainException(
                $"Currency code '{trimmed}' is not a recognized ISO 4217 currency code.");
        }

        Code = trimmed;
    }

    public static Currency Default => new(DefaultCode);

    public static Currency From(string code) => new(code);

    public override string ToString() => Code;

    public static implicit operator string(Currency currency) => currency.Code;
}
