using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RestaurantOrder.Application.Orders;

/// <summary>
/// Canonical deterministic idempotency fingerprint generator for customer and staff orders.
/// Serializes a typed canonical payload to deterministic JSON without manual string delimiters,
/// neutralizing note injection, distinguishing null from "null", preserving duplicate items,
/// and ensuring order-independence of items and modifiers.
/// </summary>
public static class OrderFingerprintGenerator
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public sealed record CanonicalModifier(string GroupId, string OptionId);

    public sealed record CanonicalOrderItem(
        string MenuItemId,
        string? ItemVariantId,
        int Quantity,
        string? CustomerNote,
        long? ClientExpectedUnitPriceMinorUnits,
        IReadOnlyList<CanonicalModifier> Modifiers);

    public sealed record CanonicalOrderPayload(
        string SessionId,
        IReadOnlyList<CanonicalOrderItem> Items,
        string? PricingPolicyFingerprint,
        long? ClientExpectedTotalMinorUnits);

    public static string ComputeCustomerOrderFingerprint(
        Guid sessionId,
        IEnumerable<CustomerOrderItemRequest> items,
        string? pricingPolicyFingerprint = null,
        long? clientExpectedTotalMinorUnits = null)
    {
        var canonicalItems = items.Select(i => CanonicalizeItem(
            i.MenuItemId,
            i.ItemVariantId,
            i.Quantity,
            i.CustomerNote,
            i.ClientExpectedUnitPriceMinorUnits,
            i.SelectedModifiers?.Select(m => (m.ModifierGroupId, m.ModifierOptionId))));

        return ComputeFingerprint(sessionId, canonicalItems, pricingPolicyFingerprint, clientExpectedTotalMinorUnits);
    }

    public static string ComputeStaffOrderFingerprint(
        Guid sessionId,
        IEnumerable<StaffOrderItemRequest> items,
        string? pricingPolicyFingerprint = null,
        long? clientExpectedTotalMinorUnits = null)
    {
        var canonicalItems = items.Select(i => CanonicalizeItem(
            i.MenuItemId,
            i.ItemVariantId,
            i.Quantity,
            i.CustomerNote,
            i.ClientExpectedUnitPriceMinorUnits,
            i.SelectedModifiers?.Select(m => (m.ModifierGroupId, m.ModifierOptionId))));

        return ComputeFingerprint(sessionId, canonicalItems, pricingPolicyFingerprint, clientExpectedTotalMinorUnits);
    }

    private static CanonicalOrderItem CanonicalizeItem(
        Guid menuItemId,
        Guid? itemVariantId,
        int quantity,
        string? customerNote,
        long? clientExpectedUnitPriceMinorUnits,
        IEnumerable<(Guid GroupId, Guid OptionId)>? selectedModifiers)
    {
        var sortedModifiers = (selectedModifiers ?? Enumerable.Empty<(Guid GroupId, Guid OptionId)>())
            .OrderBy(m => m.GroupId)
            .ThenBy(m => m.OptionId)
            .Select(m => new CanonicalModifier(m.GroupId.ToString("D"), m.OptionId.ToString("D")))
            .ToList();

        return new CanonicalOrderItem(
            menuItemId.ToString("D"),
            itemVariantId?.ToString("D"),
            quantity,
            NormalizeNote(customerNote),
            clientExpectedUnitPriceMinorUnits,
            sortedModifiers);
    }

    private static string ComputeFingerprint(
        Guid sessionId,
        IEnumerable<CanonicalOrderItem> items,
        string? pricingPolicyFingerprint,
        long? clientExpectedTotalMinorUnits)
    {
        // Sort items deterministically based on their individual canonical JSON representation
        // Duplicate items are preserved because each item maps to its own list element
        var sortedItems = items
            .Select(item => (Item: item, Json: JsonSerializer.Serialize(item, CanonicalJsonOptions)))
            .OrderBy(pair => pair.Json, StringComparer.Ordinal)
            .Select(pair => pair.Item)
            .ToList();

        var normalizedPolicyFingerprint = NormalizePolicyFingerprint(pricingPolicyFingerprint);

        var payload = new CanonicalOrderPayload(
            sessionId.ToString("D"),
            sortedItems,
            normalizedPolicyFingerprint,
            clientExpectedTotalMinorUnits);

        var payloadJson = JsonSerializer.Serialize(payload, CanonicalJsonOptions);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string? NormalizeNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        return note.Trim();
    }

    private static string? NormalizePolicyFingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        return fingerprint.Trim().ToLowerInvariant();
    }
}
