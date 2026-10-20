using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Orders.Pricing;

/// <summary>
/// Domain exception thrown when authoritative order pricing or catalog validation fails.
/// Carries structured machine-readable error codes and safe localized messages.
/// </summary>
public sealed class OrderPricingException : DomainException
{
    public const string CodePriceChanged = "ORDER_PRICE_CHANGED";
    public const string CodeItemOutOfStock = "ITEM_OUT_OF_STOCK";
    public const string CodeOrderTakingDisabled = "ORDER_TAKING_DISABLED";
    public const string CodeInvalidModifierSelection = "INVALID_MODIFIER_SELECTION";
    public const string CodeDuplicateModifier = "DUPLICATE_MODIFIER";
    public const string CodeInvalidVariant = "INVALID_VARIANT";
    public const string CodeSessionNotActive = "SESSION_NOT_ACTIVE";
    public const string CodeCurrencyMismatch = "CURRENCY_MISMATCH";
    public const string CodeItemNotActive = "ITEM_NOT_ACTIVE";
    public const string CodePricingPolicyChanged = "ORDER_PRICING_POLICY_CHANGED";
    public const string CodeOrderTotalChanged = "ORDER_TOTAL_CHANGED";

    public string ErrorCode { get; }
    public MenuItemId? MenuItemId { get; }
    public ItemVariantId? ItemVariantId { get; }
    public long? ExpectedUnitPriceMinorUnits { get; }
    public long? ActualUnitPriceMinorUnits { get; }

    private OrderPricingException(
        string errorCode,
        string message,
        MenuItemId? menuItemId = null,
        ItemVariantId? itemVariantId = null,
        long? expectedUnitPriceMinorUnits = null,
        long? actualUnitPriceMinorUnits = null)
        : base(message)
    {
        ErrorCode = errorCode;
        MenuItemId = menuItemId;
        ItemVariantId = itemVariantId;
        ExpectedUnitPriceMinorUnits = expectedUnitPriceMinorUnits;
        ActualUnitPriceMinorUnits = actualUnitPriceMinorUnits;
    }

    public static OrderPricingException PriceChanged(
        MenuItemId menuItemId,
        ItemVariantId? itemVariantId,
        long expectedPrice,
        long actualPrice)
    {
        return new OrderPricingException(
            CodePriceChanged,
            $"Ürünün güncel fiyatı değişti. Beklenen: {expectedPrice}, Güncel: {actualPrice}.",
            menuItemId,
            itemVariantId,
            expectedPrice,
            actualPrice);
    }

    public static OrderPricingException ItemOutOfStock(MenuItemId menuItemId, ItemVariantId? itemVariantId = null)
    {
        return new OrderPricingException(
            CodeItemOutOfStock,
            "Seçilen ürün veya varyant stokta bulunmuyor (Hızlı-86).",
            menuItemId,
            itemVariantId);
    }

    public static OrderPricingException OrderTakingDisabled()
    {
        return new OrderPricingException(
            CodeOrderTakingDisabled,
            "Şubede şu anda sipariş alımı kapalıdır.");
    }

    public static OrderPricingException InvalidModifierSelection(string details, MenuItemId? menuItemId = null)
    {
        return new OrderPricingException(
            CodeInvalidModifierSelection,
            $"Geçersiz modifier seçimi: {details}",
            menuItemId);
    }

    public static OrderPricingException DuplicateModifier(Guid modifierOptionId, MenuItemId? menuItemId = null)
    {
        return new OrderPricingException(
            CodeDuplicateModifier,
            $"Aynı sipariş kaleminde yinelenen modifier seçilemez: {modifierOptionId}",
            menuItemId);
    }

    public static OrderPricingException InvalidVariant(MenuItemId menuItemId, ItemVariantId itemVariantId)
    {
        return new OrderPricingException(
            CodeInvalidVariant,
            $"Varyant ({itemVariantId.Value}) seçilen menü öğesine ({menuItemId.Value}) ait değil.",
            menuItemId,
            itemVariantId);
    }

    public static OrderPricingException SessionNotActive(Guid sessionId)
    {
        return new OrderPricingException(
            CodeSessionNotActive,
            $"Oturum ({sessionId}) aktif durumda değil veya bulunamadı.");
    }

    public static OrderPricingException CurrencyMismatch(string expectedCurrency, string actualCurrency)
    {
        return new OrderPricingException(
            CodeCurrencyMismatch,
            $"Sipariş para birimi ({actualCurrency}) şube para birimi ({expectedCurrency}) ile uyuşmuyor.");
    }

    public static OrderPricingException ItemNotActive(MenuItemId menuItemId)
    {
        return new OrderPricingException(
            CodeItemNotActive,
            $"Menü öğesi ({menuItemId.Value}) veya bağlı olduğu menü/kategori şu anda aktif değil.",
            menuItemId);
    }

    public static OrderPricingException PricingPolicyChanged(string? expectedFingerprint, string actualFingerprint)
    {
        return new OrderPricingException(
            CodePricingPolicyChanged,
            $"Fiyatlandırma veya vergi/servis politikası değişti. Lütfen sepetinizi güncelleyin. Beklenen: '{expectedFingerprint}', Güncel: '{actualFingerprint}'.");
    }

    public static OrderPricingException OrderTotalChanged(long expectedTotal, long actualTotal)
    {
        return new OrderPricingException(
            CodeOrderTotalChanged,
            $"Sipariş genel toplamı değişti. Beklenen: {expectedTotal}, Güncel: {actualTotal}.",
            expectedUnitPriceMinorUnits: expectedTotal,
            actualUnitPriceMinorUnits: actualTotal);
    }
}
