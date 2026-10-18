using System.Security.Cryptography;
using System.Text;

namespace RestaurantOrder.Application.Orders;

/// <summary>
/// Canonical deterministic idempotency fingerprint generator for customer and staff orders.
/// Guarantees order-independent normalization, full payload capture, and SHA-256 64-char lowercase hex.
/// </summary>
public static class OrderFingerprintGenerator
{
    public static string ComputeCustomerOrderFingerprint(Guid sessionId, IEnumerable<CustomerOrderItemRequest> items)
    {
        var canonicalItems = items.Select(i => CanonicalizeItem(
            i.MenuItemId,
            i.ItemVariantId,
            i.Quantity,
            i.CustomerNote,
            i.ClientExpectedUnitPriceMinorUnits,
            i.SelectedModifiers?.Select(m => (m.ModifierGroupId, m.ModifierOptionId))));

        return ComputeFingerprint(sessionId, canonicalItems);
    }

    public static string ComputeStaffOrderFingerprint(Guid sessionId, IEnumerable<StaffOrderItemRequest> items)
    {
        var canonicalItems = items.Select(i => CanonicalizeItem(
            i.MenuItemId,
            i.ItemVariantId,
            i.Quantity,
            i.CustomerNote,
            i.ClientExpectedUnitPriceMinorUnits,
            i.SelectedModifiers?.Select(m => (m.ModifierGroupId, m.ModifierOptionId))));

        return ComputeFingerprint(sessionId, canonicalItems);
    }

    private static string CanonicalizeItem(
        Guid menuItemId,
        Guid? itemVariantId,
        int quantity,
        string? customerNote,
        long? clientExpectedUnitPriceMinorUnits,
        IEnumerable<(Guid GroupId, Guid OptionId)>? selectedModifiers)
    {
        var variantStr = itemVariantId.HasValue ? itemVariantId.Value.ToString("D") : "null";
        var normalizedNote = string.IsNullOrWhiteSpace(customerNote) ? "null" : customerNote.Trim();
        var expPriceStr = clientExpectedUnitPriceMinorUnits.HasValue ? clientExpectedUnitPriceMinorUnits.Value.ToString() : "null";

        var modifierPairs = selectedModifiers?
            .OrderBy(m => m.GroupId)
            .ThenBy(m => m.OptionId)
            .Select(m => $"{m.GroupId:D}>{m.OptionId:D}") ?? Enumerable.Empty<string>();

        var modifierStr = string.Join(";", modifierPairs);

        return $"{menuItemId:D}|{variantStr}|{quantity}|{normalizedNote}|{expPriceStr}|[{modifierStr}]";
    }

    private static string ComputeFingerprint(Guid sessionId, IEnumerable<string> canonicalItems)
    {
        var sortedItems = canonicalItems.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var payload = $"session:{sessionId:D}|items:[{string.Join(",", sortedItems)}]";

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
