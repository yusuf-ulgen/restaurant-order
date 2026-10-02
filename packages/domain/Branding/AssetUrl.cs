using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branding;

/// <summary>
/// Immutable value object representing a validated, secure brand asset URL.
/// Strictly permits only safe relative paths (starting with '/' without '..' traversals)
/// or verified HTTPS URLs ('https://').
/// Prohibits insecure URL schemes (http:, data:, javascript:, vbscript:, file:),
/// and characters that could lead to HTML/CSS injection.
/// </summary>
public readonly record struct AssetUrl
{
    public const int MaxLength = 500;

    public string Value { get; }

    public AssetUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Asset URL cannot be null, empty, or whitespace.");
        }

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            throw new DomainException($"Asset URL cannot exceed {MaxLength} characters.");
        }

        // Check for HTML/Script injection characters
        if (trimmed.Contains('<') || trimmed.Contains('>') || trimmed.Contains('"') || trimmed.Contains('\''))
        {
            throw new DomainException("Asset URL contains invalid or potentially unsafe characters.");
        }

        // Relative path validation: must start with single '/' and not contain '..' or '//' or backslashes
        if (trimmed.StartsWith("/"))
        {
            if (trimmed.StartsWith("//") || trimmed.Contains("/../") || trimmed.EndsWith("/..") || trimmed.Contains("..") || trimmed.Contains('\\'))
            {
                throw new DomainException("Relative asset URL cannot contain path traversal, protocol-relative sequences, or backslashes.");
            }

            Value = trimmed;
            return;
        }

        // Absolute URL validation: must be well-formed HTTPS only
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException(
                    $"Insecure URL scheme '{uri.Scheme}' is prohibited. Only HTTPS absolute URLs or safe relative paths are accepted.");
            }

            Value = uri.ToString();
            return;
        }

        throw new DomainException(
            "Asset URL must be a valid relative path starting with '/' or an absolute 'https://' URL.");
    }

    public static AssetUrl? FromNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new AssetUrl(value);
    }

    public override string ToString() => Value;

    public static implicit operator string(AssetUrl url) => url.Value;
}
