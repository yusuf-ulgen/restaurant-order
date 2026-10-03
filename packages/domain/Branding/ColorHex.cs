using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branding;

/// <summary>
/// Immutable value object representing a validated CSS hex color code (#RGB or #RRGGBB).
/// Rejects arbitrary CSS strings, named colors, RGB/HSL functions, and 8-digit alpha values to prevent style injection.
/// Normalizes 3-character hex shorthand (#rgb) into canonical 6-character format (#rrggbb) in lowercase.
/// </summary>
public readonly record struct ColorHex
{
    private static readonly Regex HexRegex = new(
        @"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Value { get; }

    public ColorHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Hex color cannot be null, empty, or whitespace.");
        }

        var trimmed = value.Trim();

        if (!HexRegex.IsMatch(trimmed))
        {
            throw new DomainException(
                $"Color '{trimmed}' is not a valid hex color. Must be in #RGB or #RRGGBB format (e.g. #FFFFFF or #000).");
        }

        // Canonical normalization to lowercase #rrggbb
        if (trimmed.Length == 4) // #rgb
        {
            var r = trimmed[1];
            var g = trimmed[2];
            var b = trimmed[3];
            Value = $"#{r}{r}{g}{g}{b}{b}".ToLowerInvariant();
        }
        else
        {
            Value = trimmed.ToLowerInvariant();
        }
    }

    public static ColorHex? FromNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new ColorHex(value);
    }

    public override string ToString() => Value;

    public static implicit operator string(ColorHex color) => color.Value;
}
