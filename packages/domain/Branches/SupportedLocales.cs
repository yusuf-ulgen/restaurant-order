using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Domain validator and serializer for default and supported branch locales.
/// Prevents duplicates, format malformations, and ensures default locale presence.
/// </summary>
public static class SupportedLocales
{
    private static readonly Regex LocaleRegex = new(
        @"^[a-z]{2}(-[A-Z]{2})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const string DefaultLocale = "tr-TR";

    public static readonly FrozenSet<string> PredefinedLocales = new[]
    {
        "tr-TR", "en-US", "en-GB", "de-DE", "fr-FR", "es-ES", "it-IT", "ru-RU", "ar-SA", "zh-CN"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static void Validate(string defaultLocale, IEnumerable<string> supportedLocales)
    {
        if (string.IsNullOrWhiteSpace(defaultLocale))
        {
            throw new DomainException("Default locale cannot be null, empty, or whitespace.");
        }

        var trimmedDefault = defaultLocale.Trim();
        if (!LocaleRegex.IsMatch(trimmedDefault))
        {
            throw new DomainException($"Default locale '{trimmedDefault}' is not a valid BCP 47 language tag (e.g., 'tr-TR', 'en-US').");
        }

        if (supportedLocales == null)
        {
            throw new DomainException("Supported locales list cannot be null.");
        }

        var list = supportedLocales.ToList();
        if (list.Count == 0)
        {
            throw new DomainException("Supported locales cannot be empty.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var loc in list)
        {
            if (string.IsNullOrWhiteSpace(loc))
            {
                throw new DomainException("Locale entry cannot be empty or whitespace.");
            }

            var trimmed = loc.Trim();
            if (!LocaleRegex.IsMatch(trimmed))
            {
                throw new DomainException($"Locale '{trimmed}' is not a valid BCP 47 language tag.");
            }

            if (!seen.Add(trimmed))
            {
                throw new DomainException($"Duplicate locale '{trimmed}' is not allowed in supported locales.");
            }
        }

        if (!seen.Contains(trimmedDefault))
        {
            throw new DomainException($"Default locale '{trimmedDefault}' must be present in the supported locales list.");
        }
    }

    public static string Serialize(IEnumerable<string> supportedLocales)
    {
        var distinct = supportedLocales.Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return JsonSerializer.Serialize(distinct);
    }

    public static IReadOnlyList<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new[] { DefaultLocale };
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(json);
            return parsed != null && parsed.Length > 0 ? parsed : new[] { DefaultLocale };
        }
        catch
        {
            return new[] { DefaultLocale };
        }
    }
}
