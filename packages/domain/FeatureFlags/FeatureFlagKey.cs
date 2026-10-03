using System.Collections.Frozen;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.FeatureFlags;

/// <summary>
/// Type-safe catalog of recognized system feature flag keys.
/// Unknown keys are strictly rejected at the domain boundary.
/// </summary>
public static class FeatureFlagKey
{
    public const string CustomerQrOrdering = "CustomerQrOrdering";
    public const string CustomerServiceRequests = "CustomerServiceRequests";
    public const string Tips = "Tips";
    public const string SplitBilling = "SplitBilling";
    public const string OnlinePayments = "OnlinePayments";
    public const string KitchenDisplay = "KitchenDisplay";
    public const string BarDisplay = "BarDisplay";
    public const string Reservations = "Reservations";
    public const string KioskMode = "KioskMode";

    public static readonly FrozenSet<string> All = new[]
    {
        CustomerQrOrdering,
        CustomerServiceRequests,
        Tips,
        SplitBilling,
        OnlinePayments,
        KitchenDisplay,
        BarDisplay,
        Reservations,
        KioskMode
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Base system default values. Features without an implementation are disabled by default.
    /// </summary>
    public static readonly FrozenDictionary<string, bool> SystemDefaults = new Dictionary<string, bool>
    {
        [CustomerQrOrdering] = true,
        [CustomerServiceRequests] = true,
        [KitchenDisplay] = true,
        [BarDisplay] = true,
        [Tips] = false,
        [SplitBilling] = false,
        [OnlinePayments] = false,
        [Reservations] = false,
        [KioskMode] = false
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool IsKnown(string key) =>
        !string.IsNullOrWhiteSpace(key) && All.Contains(key);

    public static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !All.Contains(key))
        {
            throw new DomainException(
                $"Unrecognized feature flag key '{key}'. Only registered catalog features are supported.");
        }
    }
}
