using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Catalog;

public partial class MenuItem
{
    private static readonly Regex SlugRegex = new(@"^[a-z0-9]+([-_][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Item name cannot be empty.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 100)
        {
            throw new DomainException("Item name must be between 1 and 100 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Item name cannot contain HTML or markup tags.");
        }

        return trimmed;
    }

    public static string ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainException("Item slug cannot be empty.");
        }

        var normalized = slug.Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 50)
        {
            throw new DomainException("Item slug must be between 1 and 50 characters.");
        }

        if (!SlugRegex.IsMatch(normalized))
        {
            throw new DomainException($"Invalid item slug format '{slug}'. Use lowercase letters, digits, and hyphens/underscores.");
        }

        return normalized;
    }

    public static string? ValidateShortDescription(string? shortDesc)
    {
        if (string.IsNullOrWhiteSpace(shortDesc))
        {
            return null;
        }

        var trimmed = shortDesc.Trim();
        if (trimmed.Length > 200)
        {
            throw new DomainException("Short description cannot exceed 200 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Short description cannot contain HTML or markup tags.");
        }

        return trimmed;
    }

    public static string? ValidateFullDescription(string? fullDesc)
    {
        if (string.IsNullOrWhiteSpace(fullDesc))
        {
            return null;
        }

        var trimmed = fullDesc.Trim();
        if (trimmed.Length > 2000)
        {
            throw new DomainException("Full description cannot exceed 2000 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Full description cannot contain HTML or markup tags.");
        }

        return trimmed;
    }

    public static string? ValidateImageUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var trimmed = imageUrl.Trim();
        if (trimmed.Length > 500)
        {
            throw new DomainException("ImageUrl cannot exceed 500 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("ImageUrl cannot contain HTML or markup tags.");
        }

        var lower = trimmed.ToLowerInvariant();
        if (lower.StartsWith("javascript:") || lower.StartsWith("data:") || lower.StartsWith("vbscript:"))
        {
            throw new DomainException("ImageUrl cannot contain javascript or data schemes.");
        }

        if (trimmed.StartsWith("//"))
        {
            throw new DomainException("Protocol-relative URLs are not permitted.");
        }

        if (trimmed.StartsWith("/"))
        {
            return trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        throw new DomainException("ImageUrl must be a secure relative path (starting with '/') or an HTTPS URL ('https://').");
    }
}
