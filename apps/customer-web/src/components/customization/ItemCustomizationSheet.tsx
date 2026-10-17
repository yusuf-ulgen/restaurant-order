import React, { useState, useEffect, useMemo, useCallback } from 'react';
import type { RuntimeMenuItem, DraftCartLine } from '@restaurant-order/contracts';
import { BottomSheet, Badge, Button } from '@restaurant-order/ui';
import { formatCurrency, sanitizeImageUrl } from '../../utils/menuUtils';
import {
  normalizeCustomerNote,
  generateConfigurationSignature,
  calculateItemPrice,
  validateCustomization,
} from '../../utils/customizationUtils';
import { VariantSelector } from './VariantSelector';
import { ModifierGroupSection } from './ModifierGroupSection';
import { QuantitySelector } from './QuantitySelector';
import { CustomerNoteInput } from './CustomerNoteInput';
import { PricePreviewBar } from './PricePreviewBar';

export interface ItemCustomizationSheetProps {
  item: RuntimeMenuItem | null;
  currency: string;
  isOpen: boolean;
  onClose: () => void;
  avoidedAllergens?: string[];
  onAddToCart?: (cartLine: DraftCartLine) => void;
}

export const ItemCustomizationSheet: React.FC<ItemCustomizationSheetProps> = ({
  item,
  currency,
  isOpen,
  onClose,
  avoidedAllergens = [],
  onAddToCart,
}) => {
  const [selectedVariantId, setSelectedVariantId] = useState<string | null>(null);
  const [selectedModifiers, setSelectedModifiers] = useState<Record<string, string[]>>({});
  const [quantity, setQuantity] = useState<number>(1);
  const [customerNote, setCustomerNote] = useState<string>('');
  const [groupErrors, setGroupErrors] = useState<Record<string, string>>({});
  const [imageFailed, setImageFailed] = useState<boolean>(false);

  // Initialize and reset state when item or isOpen changes
  const resetCustomizationState = useCallback(() => {
    if (!item) return;

    setImageFailed(false);
    setQuantity(1);
    setCustomerNote('');
    setGroupErrors({});

    // 1. Variant initialization:
    // If variants exist, pick default ONLY if it is available. If default is out of stock, do not auto-select another.
    if (item.variants && item.variants.length > 0) {
      const defaultVariant = item.variants.find((v) => v.isDefault && v.isAvailable);
      setSelectedVariantId(defaultVariant ? defaultVariant.id : null);
    } else {
      setSelectedVariantId(null);
    }

    // 2. Modifiers initialization:
    // Select default options up to group.maxSelections
    const initialModifiers: Record<string, string[]> = {};
    for (const group of item.modifierGroups || []) {
      const defaultOptions = (group.options || [])
        .filter((o) => o.isDefault)
        .slice(0, group.maxSelections)
        .map((o) => o.id);
      initialModifiers[group.id] = defaultOptions;
    }
    setSelectedModifiers(initialModifiers);
  }, [item]);

  useEffect(() => {
    if (isOpen) {
      resetCustomizationState();
    }
  }, [isOpen, resetCustomizationState]);

  // Handle modifier option toggling
  const handleToggleModifierOption = (groupId: string, optionId: string) => {
    if (!item) return;
    const group = (item.modifierGroups || []).find((g) => g.id === groupId);
    if (!group) return;

    setSelectedModifiers((prev) => {
      const currentSelected = prev[groupId] || [];
      const isAlreadySelected = currentSelected.includes(optionId);

      // Single-choice (maxSelections === 1)
      if (group.maxSelections === 1) {
        if (isAlreadySelected) {
          // If optional (minSelections === 0), allow deselection
          if (group.minSelections === 0) {
            setGroupErrors((errs) => {
              const next = { ...errs };
              delete next[groupId];
              return next;
            });
            return { ...prev, [groupId]: [] };
          }
          return prev;
        }
        // Replace selection
        setGroupErrors((errs) => {
          const next = { ...errs };
          delete next[groupId];
          return next;
        });
        return { ...prev, [groupId]: [optionId] };
      }

      // Multi-choice (maxSelections > 1)
      if (isAlreadySelected) {
        // Deselect option
        const nextSelected = currentSelected.filter((id) => id !== optionId);
        setGroupErrors((errs) => {
          const next = { ...errs };
          delete next[groupId];
          return next;
        });
        return { ...prev, [groupId]: nextSelected };
      }

      // Trying to add new selection
      if (currentSelected.length >= group.maxSelections) {
        // Do NOT silently remove old selection; show explicit error
        setGroupErrors((errs) => ({
          ...errs,
          [groupId]: `Bu gruptan en fazla ${group.maxSelections} seçim yapabilirsiniz.`,
        }));
        return prev;
      }

      // Add selection
      setGroupErrors((errs) => {
        const next = { ...errs };
        delete next[groupId];
        return next;
      });
      return { ...prev, [groupId]: [...currentSelected, optionId] };
    });
  };

  const handleSelectVariant = (variantId: string) => {
    setSelectedVariantId(variantId);
    setGroupErrors((errs) => {
      const next = { ...errs };
      delete next.variant;
      return next;
    });
  };

  // Flatten selected modifier IDs for pricing and validation
  const allSelectedModifierOptionIds = useMemo(() => {
    return Object.values(selectedModifiers).flat();
  }, [selectedModifiers]);

  // Validation
  const validationResult = useMemo(() => {
    if (!item) return { isValid: false, errors: {} };
    return validateCustomization({
      item,
      selectedVariantId,
      selectedModifiers,
    });
  }, [item, selectedVariantId, selectedModifiers]);

  // Combined errors (validation errors + interactive limit warnings)
  const combinedErrors = useMemo(() => {
    return {
      ...validationResult.errors,
      ...groupErrors,
    };
  }, [validationResult.errors, groupErrors]);

  // Dynamic price preview calculation
  const priceCalculation = useMemo(() => {
    if (!item) {
      return { unitPriceMinorUnits: 0, lineTotalMinorUnits: 0 };
    }

    try {
      return calculateItemPrice({
        item,
        selectedVariantId,
        selectedModifierOptionIds: allSelectedModifierOptionIds,
        quantity,
      });
    } catch {
      return {
        unitPriceMinorUnits: item.basePriceMinorUnits,
        lineTotalMinorUnits: item.basePriceMinorUnits * quantity,
      };
    }
  }, [item, selectedVariantId, allSelectedModifierOptionIds, quantity]);

  // Handle CTA add to cart
  const handleAddToCart = () => {
    if (!item || !validationResult.isValid || !item.isAvailable) return;

    const signature = generateConfigurationSignature({
      menuItemId: item.id,
      variantId: selectedVariantId,
      selectedModifierOptionIds: allSelectedModifierOptionIds,
      customerNote,
    });

    const draftCartLine: DraftCartLine = {
      menuItemId: item.id,
      variantId: selectedVariantId,
      selectedModifierOptionIds: [...allSelectedModifierOptionIds].sort(),
      customerNote: normalizeCustomerNote(customerNote),
      quantity,
      unitPriceMinorUnits: priceCalculation.unitPriceMinorUnits,
      lineTotalMinorUnits: priceCalculation.lineTotalMinorUnits,
      currency,
      configurationSignature: signature,
    };

    onAddToCart?.(draftCartLine);
    onClose();
  };

  if (!item) return null;

  const safeImageUrl = sanitizeImageUrl(item.imageUrl);
  const conflictingAllergens = (item.allergenTags || []).filter((a) =>
    avoidedAllergens.some((avoided) => avoided.toLowerCase() === a.toLowerCase())
  );

  return (
    <BottomSheet
      isOpen={isOpen}
      onClose={onClose}
      title={item.name}
      description={item.shortDescription || 'Ürün Bilgileri ve Özelleştirme'}
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--ro-space-4)',
          paddingBottom: 'var(--ro-space-4)',
        }}
        data-testid="menu-item-detail-sheet"
      >
        <div data-testid="item-customization-sheet" style={{ display: 'contents' }}>
        {/* Product Image & Fallback */}
        <div
          style={{
            width: '100%',
            height: '180px',
            backgroundColor: 'var(--ro-color-surface-hover)',
            borderRadius: 'var(--ro-radius-md)',
            overflow: 'hidden',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          {safeImageUrl && !imageFailed ? (
            <img
              src={safeImageUrl}
              alt={item.name}
              onError={() => setImageFailed(true)}
              style={{ width: '100%', height: '100%', objectFit: 'cover' }}
            />
          ) : (
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                color: 'var(--ro-color-text-muted)',
              }}
            >
              <span style={{ fontSize: '36px' }} role="img" aria-label="Görsel yok">
                🍽️
              </span>
              <span style={{ fontSize: 'var(--ro-font-size-xs)' }}>Lezzet Görseli</span>
            </div>
          )}
        </div>

        {/* Item Header & Full Description */}
        <div>
          <div
            style={{
              display: 'flex',
              alignItems: 'baseline',
              justifyContent: 'space-between',
              marginBottom: 'var(--ro-space-1)',
            }}
          >
            <h3
              style={{
                margin: 0,
                fontSize: 'var(--ro-font-size-lg)',
                fontWeight: 'var(--ro-font-weight-bold)',
                color: 'var(--ro-color-text-primary)',
              }}
            >
              {item.name}
            </h3>
            <span
              style={{
                fontSize: 'var(--ro-font-size-md)',
                fontWeight: 'var(--ro-font-weight-bold)',
                color: 'var(--ro-color-text-primary)',
              }}
            >
              {formatCurrency(item.basePriceMinorUnits, currency)}
            </span>
          </div>

          <p
            style={{
              margin: 0,
              fontSize: 'var(--ro-font-size-sm)',
              color: 'var(--ro-color-text-secondary)',
              lineHeight: 1.5,
            }}
          >
            {item.fullDescription || item.shortDescription || 'Özel reçetesiyle taze hazırlanır.'}
          </p>
        </div>

        {/* Dietary, Allergen and Spicy info */}
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--ro-space-2)' }}>
          {item.spicyLevel > 0 && (
            <Badge variant="neutral">
              {'🌶️'.repeat(Math.min(item.spicyLevel, 3))} Acı Seviyesi {item.spicyLevel}
            </Badge>
          )}

          {(item.dietaryTags || []).map((tag) => (
            <Badge key={tag} variant="neutral">
              {tag}
            </Badge>
          ))}

          {(item.allergenTags || []).map((allergen) => {
            const isConflicting = conflictingAllergens.includes(allergen);
            return (
              <Badge key={allergen} variant={isConflicting ? 'warning' : 'neutral'}>
                {isConflicting ? `⚠️ ${allergen} içerir` : allergen}
              </Badge>
            );
          })}
        </div>

        {/* Out of stock banner if not available */}
        {!item.isAvailable && (
          <div
            style={{
              padding: 'var(--ro-space-3)',
              backgroundColor: 'var(--ro-color-error-subtle)',
              color: 'var(--ro-color-error-text)',
              borderRadius: 'var(--ro-radius-md)',
              fontSize: 'var(--ro-font-size-sm)',
              fontWeight: 'var(--ro-font-weight-semibold)',
              textAlign: 'center',
            }}
            data-testid="item-out-of-stock-alert"
          >
            Bu lezzet şu anda stokta bulunmamaktadır (Tükendi).
            {item.expectedAvailableAtUtc && (
              <div style={{ fontSize: 'var(--ro-font-size-xs)', marginTop: '4px', fontWeight: 'normal' }}>
                Tahmini tekrar servis: {new Date(item.expectedAvailableAtUtc).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}
              </div>
            )}
          </div>
        )}

        {/* Variant selection */}
        {item.variants && item.variants.length > 0 && (
          <VariantSelector
            variants={item.variants}
            selectedVariantId={selectedVariantId}
            onSelectVariant={handleSelectVariant}
            currency={currency}
            error={combinedErrors.variant}
          />
        )}

        {/* Modifier groups */}
        {(item.modifierGroups || []).map((group) => (
          <ModifierGroupSection
            key={group.id}
            group={group}
            selectedOptionIds={selectedModifiers[group.id] || []}
            onToggleOption={handleToggleModifierOption}
            currency={currency}
            error={combinedErrors[group.id]}
          />
        ))}

        {/* Quantity selector */}
        <QuantitySelector
          quantity={quantity}
          onChangeQuantity={setQuantity}
          min={1}
          max={99}
        />

        {/* Customer note */}
        <CustomerNoteInput
          note={customerNote}
          onChangeNote={setCustomerNote}
        />

        {/* Dynamic price preview and CTA */}
        <PricePreviewBar
          unitPriceMinorUnits={priceCalculation.unitPriceMinorUnits}
          lineTotalMinorUnits={priceCalculation.lineTotalMinorUnits}
          currency={currency}
          quantity={quantity}
          isValid={validationResult.isValid}
          isItemAvailable={item.isAvailable}
          onAddToCart={handleAddToCart}
        />

        <Button
          type="button"
          variant="secondary"
          size="md"
          onClick={onClose}
          data-testid="close-detail-sheet-btn"
          style={{ minHeight: '44px', width: '100%', marginTop: 'var(--ro-space-1)' }}
        >
          Kapat
        </Button>
        </div>
      </div>
    </BottomSheet>
  );
};
