import {
  CUSTOMER_NOTE_MAX_LENGTH,
  type RuntimeMenuItem,
  type RuntimeMenuReadModel,
} from './catalog';

/**
 * Strips non-printable ASCII/Unicode control characters (0-31 except tab/newline, and 127).
 */
export function stripControlCharacters(str: string): string {
  let result = '';
  for (let i = 0; i < str.length; i++) {
    const code = str.charCodeAt(i);
    if ((code >= 32 && code !== 127) || code === 9 || code === 10) {
      result += str[i];
    }
  }
  return result;
}

/**
 * Normalizes customer order note:
 * - Trims leading and trailing whitespace
 * - Strips control characters
 * - Converts pure whitespace to null
 * - Limits to CUSTOMER_NOTE_MAX_LENGTH
 */
export function normalizeCustomerNote(rawNote: string | null | undefined): string | null {
  if (!rawNote) return null;

  const cleaned = stripControlCharacters(rawNote);
  const trimmed = cleaned.trim();

  if (trimmed.length === 0) {
    return null;
  }

  if (trimmed.length > CUSTOMER_NOTE_MAX_LENGTH) {
    return trimmed.slice(0, CUSTOMER_NOTE_MAX_LENGTH).trim();
  }

  return trimmed;
}

/**
 * Generates a deterministic signature representing the item customization.
 */
export function generateConfigurationSignature(params: {
  menuItemId: string;
  variantId: string | null;
  selectedModifierOptionIds: string[];
  customerNote: string | null;
}): string {
  const sortedModifiers = [...params.selectedModifierOptionIds].sort();
  const normalizedNote = normalizeCustomerNote(params.customerNote) || '';
  const variantPart = params.variantId || 'none';

  return `${params.menuItemId}:${variantPart}:${sortedModifiers.join(',')}:${normalizedNote}`;
}

export interface PriceCalculationResult {
  unitPriceMinorUnits: number;
  lineTotalMinorUnits: number;
}

/**
 * Pure price calculation function for item customization preview.
 * Uses integer minor units exclusively; rejects floats and invalid values safely.
 */
export function calculateItemPrice(params: {
  item: RuntimeMenuItem;
  selectedVariantId: string | null;
  selectedModifierOptionIds: string[];
  quantity: number;
}): PriceCalculationResult {
  const { item, selectedVariantId, selectedModifierOptionIds, quantity } = params;

  if (
    !Number.isInteger(quantity) ||
    !Number.isSafeInteger(quantity) ||
    quantity < 1 ||
    quantity > 99
  ) {
    throw new Error('Geçersiz miktar (1-99 arasında olmalıdır).');
  }

  let basePrice = item.basePriceMinorUnits;

  if (selectedVariantId) {
    const variant = (item.variants || []).find((v) => v.id === selectedVariantId);
    if (!variant) {
      throw new Error(`Seçilen varyant bulunamadı: ${selectedVariantId}`);
    }
    basePrice = variant.absolutePriceMinorUnits;
  }

  if (!Number.isInteger(basePrice) || !Number.isSafeInteger(basePrice) || basePrice < 0) {
    throw new Error('Geçersiz taban fiyat değeri.');
  }

  let modifiersTotalDelta = 0;

  for (const optionId of selectedModifierOptionIds) {
    let foundOption: { priceDeltaMinorUnits: number } | null = null;

    for (const group of item.modifierGroups || []) {
      const match = (group.options || []).find((o) => o.id === optionId);
      if (match) {
        foundOption = match;
        break;
      }
    }

    if (!foundOption) {
      throw new Error(`Seçilen özelleştirme seçeneği bulunamadı: ${optionId}`);
    }

    const delta = foundOption.priceDeltaMinorUnits;
    if (!Number.isInteger(delta) || !Number.isSafeInteger(delta)) {
      throw new Error('Geçersiz fiyat farkı değeri.');
    }

    modifiersTotalDelta += delta;
  }

  const unitPrice = basePrice + modifiersTotalDelta;

  if (unitPrice < 0) {
    throw new Error('Birim fiyat negatif olamaz.');
  }

  if (!Number.isSafeInteger(unitPrice)) {
    throw new Error('Birim fiyat hesaplanırken sayısal sınır aşıldı.');
  }

  const lineTotal = unitPrice * quantity;

  if (!Number.isSafeInteger(lineTotal)) {
    throw new Error('Toplam tutar hesaplanırken sayısal sınır aşıldı.');
  }

  return {
    unitPriceMinorUnits: unitPrice,
    lineTotalMinorUnits: lineTotal,
  };
}

export interface CustomizationValidationResult {
  isValid: boolean;
  errors: Record<string, string>;
}

/**
 * Validates customization choices against item rules (variant requirement, min/max modifier bounds).
 */
export function validateCustomization(params: {
  item: RuntimeMenuItem;
  selectedVariantId: string | null;
  selectedModifiers: Record<string, string[]>;
}): CustomizationValidationResult {
  const { item, selectedVariantId, selectedModifiers } = params;
  const errors: Record<string, string> = {};

  if (item.variants && item.variants.length > 0) {
    if (!selectedVariantId) {
      errors.variant = 'Lütfen bir porsiyon/seçenek belirleyin.';
    } else {
      const variant = item.variants.find((v) => v.id === selectedVariantId);
      if (!variant) {
        errors.variant = 'Seçilen varyant geçerli değil.';
      } else if (!variant.isAvailable) {
        errors.variant = 'Seçilen seçenek şu anda stokta bulunmamaktadır.';
      }
    }
  }

  for (const group of item.modifierGroups || []) {
    const selectedCount = (selectedModifiers[group.id] || []).length;

    if (selectedCount < group.minSelections) {
      errors[group.id] = `Bu gruptan en az ${group.minSelections} seçim yapmalısınız.`;
    } else if (selectedCount > group.maxSelections) {
      errors[group.id] = `Bu gruptan en fazla ${group.maxSelections} seçim yapabilirsiniz.`;
    }
  }

  return {
    isValid: Object.keys(errors).length === 0,
    errors,
  };
}

export function findModifierGroupId(
  optionId: string,
  menuItemId: string,
  menu: RuntimeMenuReadModel | null
): string | null {
  if (!menu?.menus) return null;
  for (const m of menu.menus) {
    for (const c of m.categories) {
      const item = c.items.find((i) => i.id === menuItemId);
      if (item) {
        for (const g of item.modifierGroups) {
          if (g.options.some((o) => o.id === optionId)) {
            return g.id;
          }
        }
      }
    }
  }
  return null;
}
