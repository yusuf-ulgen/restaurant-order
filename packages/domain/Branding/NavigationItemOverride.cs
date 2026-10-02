using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branding;

/// <summary>
/// Domain model for customizing a navigation item from the fixed navigation registry.
/// Strict invariants ensure no arbitrary routes, HTML, or permissions can be injected.
/// </summary>
public sealed record NavigationItemOverride
{
    public static readonly string[] AllowedRegistryIds =
    [
        "dashboard",
        "brand-settings",
        "branch-settings",
        "operating-hours",
        "dining-areas",
        "preparation-stations",
        "feature-settings",
        "staff",
        "menu",
        "tables",
        "printers",
        "reports"
    ];

    public static readonly string[] AllowedSections =
    [
        "main",
        "operations",
        "settings",
        "system"
    ];

    private static readonly Regex DisallowedHtmlRegex = new(@"[<>]", RegexOptions.Compiled);

    public string Id { get; }
    public bool? IsVisible { get; }
    public int? Order { get; }
    public string? LabelOverride { get; }
    public string? Section { get; }
    public bool? Disabled { get; }

    public NavigationItemOverride(
        string id,
        bool? isVisible = null,
        int? order = null,
        string? labelOverride = null,
        string? section = null,
        bool? disabled = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new DomainException("Navigation item ID cannot be empty.");
        }

        var normalizedId = id.Trim().ToLowerInvariant();
        if (!AllowedRegistryIds.Contains(normalizedId))
        {
            throw new DomainException($"Unknown navigation registry ID: '{id}'. Only predefined registry items may be configured.");
        }

        Id = normalizedId;
        IsVisible = isVisible;

        if (order.HasValue && (order.Value < 1 || order.Value > 100))
        {
            throw new DomainException("Navigation order must be between 1 and 100.");
        }
        Order = order;

        if (labelOverride != null)
        {
            var trimmedLabel = labelOverride.Trim();
            if (trimmedLabel.Length > 50)
            {
                throw new DomainException("Navigation label override cannot exceed 50 characters.");
            }
            if (DisallowedHtmlRegex.IsMatch(trimmedLabel))
            {
                throw new DomainException("Navigation label override must be plain text and cannot contain HTML tags or '<' / '>' characters.");
            }
            LabelOverride = trimmedLabel.Length == 0 ? null : trimmedLabel;
        }

        if (section != null)
        {
            var normalizedSection = section.Trim().ToLowerInvariant();
            if (!AllowedSections.Contains(normalizedSection))
            {
                throw new DomainException($"Invalid navigation section: '{section}'. Allowed sections: {string.Join(", ", AllowedSections)}.");
            }
            Section = normalizedSection;
        }

        Disabled = disabled;
    }
}
