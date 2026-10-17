import type {
  DraftCartLine,
  RuntimeMenuReadModel,
  RuntimeMenuItem,
  CartValidationIssue,
} from '@restaurant-order/contracts';
import { calculateItemPrice } from '../utils/customizationUtils';

export interface CartVerificationResult {
  issues: CartValidationIssue[];
  hasBlockingIssues: boolean;
  hasPriceChanges: boolean;
}

/**
 * Validates cart lines against the live runtime menu.
 * Detects stockouts (86), removals, and price modifications without silent mutations.
 */
export function verifyCartAgainstRuntimeMenu(
  lines: DraftCartLine[],
  menuData: RuntimeMenuReadModel | null
): CartVerificationResult {
  const issues: CartValidationIssue[] = [];

  if (!menuData || !menuData.menus) {
    return { issues: [], hasBlockingIssues: false, hasPriceChanges: false };
  }

  // Flatten all items across all menus and categories into a quick lookup map
  const itemMap = new Map<string, RuntimeMenuItem>();
  for (const menu of menuData.menus) {
    for (const cat of menu.categories || []) {
      for (const item of cat.items || []) {
        itemMap.set(item.id, item);
      }
    }
  }

  for (const line of lines) {
    const item = itemMap.get(line.menuItemId);

    // 1. Item removed from menu
    if (!item) {
      issues.push({
        lineSignature: line.configurationSignature,
        type: 'item_removed',
        message: 'Bu ürün menüden kaldırılmıştır.',
      });
      continue;
    }

    // 2. Item is out of stock (86)
    if (!item.isAvailable) {
      issues.push({
        lineSignature: line.configurationSignature,
        type: 'item_unavailable',
        message: `"${item.name}" şu anda tükendi.`,
      });
    }

    // 3. Variant validation
    if (line.variantId) {
      const variant = (item.variants || []).find((v) => v.id === line.variantId);
      if (!variant) {
        issues.push({
          lineSignature: line.configurationSignature,
          type: 'variant_removed',
          message: `"${item.name}" için seçilen porsiyon/seçenek menüden kaldırılmıştır.`,
        });
      } else if (!variant.isAvailable) {
        issues.push({
          lineSignature: line.configurationSignature,
          type: 'variant_unavailable',
          message: `"${item.name}" seçeneği (${variant.name}) tükendi.`,
        });
      }
    }

    // 4. Modifier options validation
    let hasModifierRemoved = false;
    for (const optId of line.selectedModifierOptionIds) {
      let found = false;
      for (const group of item.modifierGroups || []) {
        if ((group.options || []).some((o) => o.id === optId)) {
          found = true;
          break;
        }
      }
      if (!found) {
        hasModifierRemoved = true;
        break;
      }
    }

    if (hasModifierRemoved) {
      issues.push({
        lineSignature: line.configurationSignature,
        type: 'modifier_removed',
        message: `"${item.name}" için seçilen bazı özelleştirmeler menüden kaldırılmıştır.`,
      });
    }

    // 5. Price change detection (never silently mutate before Phase 8; notify customer)
    if (item.isAvailable && !hasModifierRemoved) {
      try {
        const currentPricing = calculateItemPrice({
          item,
          selectedVariantId: line.variantId,
          selectedModifierOptionIds: line.selectedModifierOptionIds,
          quantity: 1, // calculate unit price
        });

        if (currentPricing.unitPriceMinorUnits !== line.unitPriceMinorUnits) {
          issues.push({
            lineSignature: line.configurationSignature,
            type: 'price_changed',
            message: `"${item.name}" birim fiyatı güncellendi.`,
            previousUnitPriceMinorUnits: line.unitPriceMinorUnits,
            currentUnitPriceMinorUnits: currentPricing.unitPriceMinorUnits,
          });
        }
      } catch {
        // Customization calculation failure indicates rules drift
        issues.push({
          lineSignature: line.configurationSignature,
          type: 'item_unavailable',
          message: `"${item.name}" özelleştirme kuralları güncellendi. Lütfen ürünü yeniden seçin.`,
        });
      }
    }
  }

  const hasBlockingIssues = issues.some(
    (i) =>
      i.type === 'item_removed' ||
      i.type === 'item_unavailable' ||
      i.type === 'variant_removed' ||
      i.type === 'variant_unavailable' ||
      i.type === 'modifier_removed'
  );

  const hasPriceChanges = issues.some((i) => i.type === 'price_changed');

  return {
    issues,
    hasBlockingIssues,
    hasPriceChanges,
  };
}
