using System.Text.RegularExpressions;

namespace RestaurantOrder.Domain.Common;

/// <summary>
/// Immutable value object representing a URL-safe normalized slug.
/// Enforces lowercase alphanumeric characters and single hyphens.
/// </summary>
public readonly record struct Slug
{
    private static readonly Regex SlugFormatRegex = new(
        @"^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const int MinLength = 2;
    public const int MaxLength = 64;

    public string Value { get; }

    public Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Slug cannot be null, empty, or whitespace.");
        }

        var trimmed = value.Trim();

        if (trimmed.Length < MinLength || trimmed.Length > MaxLength)
        {
            throw new DomainException(
                $"Slug length must be between {MinLength} and {MaxLength} characters. Got: {trimmed.Length}.");
        }

        if (!SlugFormatRegex.IsMatch(trimmed))
        {
            throw new DomainException(
                $"Slug '{trimmed}' is invalid. It must contain only lowercase alphanumeric characters and single hyphens without leading or trailing hyphens.");
        }

        Value = trimmed;
    }

    public static Slug From(string value) => new(value);

    public override string ToString() => Value;

    public static implicit operator string(Slug slug) => slug.Value;
}
