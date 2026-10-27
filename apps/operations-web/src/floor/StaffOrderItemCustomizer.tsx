import React, { useState, useMemo } from 'react';
import {
  RuntimeMenuItem,
  RuntimeMenuReadModel,
  calculateItemPrice,
  validateCustomization,
  normalizeCustomerNote,
  CUSTOMER_NOTE_MAX_LENGTH,
} from '@restaurant-order/contracts';
import { StaffDraftLine, StaffDraftSelectedModifier } from './staffOrderDraftStorage';

export interface StaffOrderItemCustomizerProps {
  item: RuntimeMenuItem;
  runtimeMenu: RuntimeMenuReadModel;
  initialLine?: StaffDraftLine | null;
  onSave: (line: StaffDraftLine) => void;
  onCancel: () => void;
}

export const StaffOrderItemCustomizer: React.FC<StaffOrderItemCustomizerProps> = ({
  item,
  initialLine,
  onSave,
  onCancel,
}) => {
  // 1. Initial variant
  const [selectedVariantId, setSelectedVariantId] = useState<string | null>(() => {
    if (initialLine?.variantId) return initialLine.variantId;
    if (item.variants && item.variants.length > 0) {
      const defaultVariant = item.variants.find((v) => v.isDefault && v.isAvailable) ||
        item.variants.find((v) => v.isAvailable);
      return defaultVariant ? defaultVariant.id : (item.variants[0]?.id || null);
    }
    return null;
  });

  // 2. Initial modifiers (grouped by group id)
  const [selectedModifiers, setSelectedModifiers] = useState<Record<string, string[]>>(() => {
    if (initialLine?.selectedModifiers) {
      const grouped: Record<string, string[]> = {};
      for (const sm of initialLine.selectedModifiers) {
        if (!grouped[sm.modifierGroupId]) grouped[sm.modifierGroupId] = [];
        const list = grouped[sm.modifierGroupId];
        if (list) list.push(sm.modifierOptionId);
      }
      return grouped;
    }
    const initial: Record<string, string[]> = {};
    for (const group of item.modifierGroups || []) {
      const defaultOptions = (group.options || []).filter((o) => o.isDefault).map((o) => o.id);
      if (defaultOptions.length > 0) {
        initial[group.id] = defaultOptions.slice(0, group.maxSelections);
      }
    }
    return initial;
  });

  // 3. Quantity
  const [quantity, setQuantity] = useState<number>(initialLine?.quantity || 1);

  // 4. Customer note
  const [customerNote, setCustomerNote] = useState<string>(initialLine?.customerNote || '');

  // 5. Validation error state
  const [validationErrors, setValidationErrors] = useState<Record<string, string>>({});

  // Flattened selected modifier option IDs
  const flattenedSelectedOptionIds = useMemo(() => {
    return Object.values(selectedModifiers).flat();
  }, [selectedModifiers]);

  // Dynamic price calculation using authoritative contracts helper
  const priceResult = useMemo(() => {
    try {
      return calculateItemPrice({
        item,
        selectedVariantId,
        selectedModifierOptionIds: flattenedSelectedOptionIds,
        quantity,
      });
    } catch {
      return {
        unitPriceMinorUnits: item.basePriceMinorUnits,
        lineTotalMinorUnits: item.basePriceMinorUnits * quantity,
      };
    }
  }, [item, selectedVariantId, flattenedSelectedOptionIds, quantity]);

  const handleToggleModifier = (groupId: string, optionId: string, maxSelections: number) => {
    setSelectedModifiers((prev) => {
      const current = prev[groupId] || [];
      const exists = current.includes(optionId);
      if (exists) {
        return {
          ...prev,
          [groupId]: current.filter((id) => id !== optionId),
        };
      }
      if (maxSelections === 1) {
        return {
          ...prev,
          [groupId]: [optionId],
        };
      }
      if (current.length >= maxSelections) {
        return prev;
      }
      return {
        ...prev,
        [groupId]: [...current, optionId],
      };
    });
  };

  const handleSave = () => {
    const validation = validateCustomization({
      item,
      selectedVariantId,
      selectedModifiers,
    });

    if (!validation.isValid) {
      setValidationErrors(validation.errors);
      return;
    }

    const selectedVariant = (item.variants || []).find((v) => v.id === selectedVariantId);

    const detailedModifiers: StaffDraftSelectedModifier[] = [];
    for (const group of item.modifierGroups || []) {
      const selectedIds = selectedModifiers[group.id] || [];
      for (const optId of selectedIds) {
        const opt = (group.options || []).find((o) => o.id === optId);
        if (opt) {
          detailedModifiers.push({
            modifierGroupId: group.id,
            modifierOptionId: opt.id,
            name: `${group.name}: ${opt.name}`,
            priceDeltaMinorUnits: opt.priceDeltaMinorUnits,
          });
        }
      }
    }

    const cleanedNote = normalizeCustomerNote(customerNote);

    const line: StaffDraftLine = {
      id: initialLine?.id || `draft-line-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
      menuItemId: item.id,
      itemName: item.name,
      variantId: selectedVariantId,
      variantName: selectedVariant ? selectedVariant.name : null,
      selectedModifierOptionIds: flattenedSelectedOptionIds,
      selectedModifiers: detailedModifiers,
      quantity,
      customerNote: cleanedNote,
      unitPriceMinorUnits: priceResult.unitPriceMinorUnits,
      lineTotalMinorUnits: priceResult.lineTotalMinorUnits,
    };

    onSave(line);
  };

  return (
    <div
      role="dialog"
      aria-labelledby="item-customizer-title"
      style={{
        position: 'fixed',
        inset: 0,
        backgroundColor: 'rgba(0,0,0,0.6)',
        display: 'flex',
        alignItems: 'flex-end',
        justifyContent: 'center',
        zIndex: 1100,
      }}
    >
      <div
        style={{
          backgroundColor: 'var(--ro-color-surface, #ffffff)',
          color: 'var(--ro-color-text, #111827)',
          width: '100%',
          maxWidth: '560px',
          maxHeight: '90vh',
          borderTopLeftRadius: '16px',
          borderTopRightRadius: '16px',
          display: 'flex',
          flexDirection: 'column',
          boxShadow: '0 -4px 20px rgba(0,0,0,0.15)',
          overflow: 'hidden',
        }}
      >
        <div style={{ padding: '16px 20px', borderBottom: '1px solid var(--ro-color-border, #e5e7eb)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <h3 id="item-customizer-title" style={{ margin: 0, fontSize: '18px', fontWeight: 600 }}>{item.name}</h3>
            <span style={{ fontSize: '14px', color: 'var(--ro-color-text-muted, #6b7280)' }}>₺{(priceResult.unitPriceMinorUnits / 100).toFixed(2)}</span>
          </div>
          <button type="button" onClick={onCancel} aria-label="Kapat" style={{ background: 'transparent', border: 'none', fontSize: '20px', cursor: 'pointer', color: 'var(--ro-color-text-muted, #6b7280)', padding: '4px 8px' }}>✕</button>
        </div>

        <div style={{ padding: '20px', overflowY: 'auto', flex: 1, display: 'flex', flexDirection: 'column', gap: '16px' }}>
          {/* Variants */}
          {item.variants && item.variants.length > 0 && (
            <div>
              <label style={{ display: 'block', fontWeight: 600, marginBottom: '8px', fontSize: '14px' }}>Porsiyon / Seçenek:</label>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
                {item.variants.map((v) => {
                  const isSelected = selectedVariantId === v.id;
                  const isAvailable = v.isAvailable;
                  return (
                    <button
                      key={v.id}
                      type="button"
                      disabled={!isAvailable}
                      onClick={() => {
                        setSelectedVariantId(v.id);
                        setValidationErrors((prev) => ({ ...prev, variant: '' }));
                      }}
                      style={{
                        padding: '8px 14px',
                        borderRadius: '8px',
                        border: isSelected ? '2px solid var(--ro-color-primary, #2563eb)' : '1px solid var(--ro-color-border, #e5e7eb)',
                        backgroundColor: isSelected ? 'var(--ro-color-primary-subtle, #eff6ff)' : 'transparent',
                        color: isSelected ? 'var(--ro-color-primary, #2563eb)' : 'inherit',
                        opacity: isAvailable ? 1 : 0.4,
                        cursor: isAvailable ? 'pointer' : 'not-allowed',
                        fontWeight: isSelected ? 600 : 400,
                        fontSize: '13px',
                      }}
                    >
                      {v.name} (₺{(v.absolutePriceMinorUnits / 100).toFixed(2)})
                      {!isAvailable && ' (Tükendi)'}
                    </button>
                  );
                })}
              </div>
              {validationErrors.variant && (
                <div style={{ color: 'var(--ro-color-error, #dc2626)', fontSize: '12px', marginTop: '4px' }}>{validationErrors.variant}</div>
              )}
            </div>
          )}

          {/* Modifier Groups */}
          {(item.modifierGroups || []).map((group) => {
            const currentSelected = selectedModifiers[group.id] || [];
            const isSingle = group.maxSelections === 1;
            const error = validationErrors[group.id];

            return (
              <div key={group.id} style={{ borderTop: '1px solid var(--ro-color-border, #e5e7eb)', paddingTop: '12px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                  <span style={{ fontWeight: 600, fontSize: '14px' }}>{group.name}</span>
                  <span style={{ fontSize: '12px', color: 'var(--ro-color-text-muted, #6b7280)' }}>
                    {group.minSelections > 0 ? `Zorunlu (Min: ${group.minSelections}, Max: ${group.maxSelections})` : `İsteğe bağlı (Max: ${group.maxSelections})`}
                  </span>
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                  {(group.options || []).map((opt) => {
                    const isChecked = currentSelected.includes(opt.id);
                    return (
                      <label
                        key={opt.id}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'space-between',
                          padding: '8px 12px',
                          borderRadius: '6px',
                          backgroundColor: isChecked ? 'var(--ro-color-primary-subtle, #eff6ff)' : 'transparent',
                          cursor: 'pointer',
                          fontSize: '13px',
                        }}
                      >
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                          <input
                            type={isSingle ? 'radio' : 'checkbox'}
                            name={`modifier-group-${group.id}`}
                            checked={isChecked}
                            onChange={() => handleToggleModifier(group.id, opt.id, group.maxSelections)}
                          />
                          <span>{opt.name}</span>
                        </div>
                        {opt.priceDeltaMinorUnits > 0 && (
                          <span style={{ color: 'var(--ro-color-text-muted, #6b7280)', fontSize: '12px' }}>
                            +₺{(opt.priceDeltaMinorUnits / 100).toFixed(2)}
                          </span>
                        )}
                      </label>
                    );
                  })}
                </div>
                {error && (
                  <div style={{ color: 'var(--ro-color-error, #dc2626)', fontSize: '12px', marginTop: '4px' }}>
                    {error}
                  </div>
                )}
              </div>
            );
          })}

          {/* Customer Note */}
          <div style={{ borderTop: '1px solid var(--ro-color-border, #e5e7eb)', paddingTop: '12px' }}>
            <label htmlFor="customer-note-input" style={{ display: 'block', fontWeight: 600, marginBottom: '6px', fontSize: '14px' }}>
              Müşteri / Garson Notu (isteğe bağlı):
            </label>
            <input
              id="customer-note-input"
              type="text"
              maxLength={CUSTOMER_NOTE_MAX_LENGTH}
              value={customerNote}
              onChange={(e) => setCustomerNote(e.target.value)}
              placeholder="Örn: Az tuzlu, sosu ayrı olsun..."
              style={{
                width: '100%',
                padding: '8px 12px',
                borderRadius: '8px',
                border: '1px solid var(--ro-color-border, #e5e7eb)',
                fontSize: '13px',
                boxSizing: 'border-box',
              }}
            />
          </div>

          {/* Quantity */}
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              borderTop: '1px solid var(--ro-color-border, #e5e7eb)',
              paddingTop: '14px',
            }}
          >
            <span style={{ fontWeight: 600, fontSize: '14px' }}>Adet:</span>
            <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
              <button
                type="button"
                disabled={quantity <= 1}
                onClick={() => setQuantity((q) => Math.max(1, q - 1))}
                aria-label="Adet Azalt"
                style={{
                  width: '36px',
                  height: '36px',
                  borderRadius: '50%',
                  border: '1px solid var(--ro-color-border, #e5e7eb)',
                  backgroundColor: 'transparent',
                  cursor: quantity > 1 ? 'pointer' : 'not-allowed',
                  fontSize: '18px',
                  fontWeight: 'bold',
                }}
              >
                -
              </button>
              <span style={{ fontSize: '16px', fontWeight: 600, minWidth: '24px', textAlign: 'center' }}>
                {quantity}
              </span>
              <button
                type="button"
                disabled={quantity >= 99}
                onClick={() => setQuantity((q) => Math.min(99, q + 1))}
                aria-label="Adet Artır"
                style={{
                  width: '36px',
                  height: '36px',
                  borderRadius: '50%',
                  border: '1px solid var(--ro-color-border, #e5e7eb)',
                  backgroundColor: 'transparent',
                  cursor: quantity < 99 ? 'pointer' : 'not-allowed',
                  fontSize: '18px',
                  fontWeight: 'bold',
                }}
              >
                +
              </button>
            </div>
          </div>
        </div>

        {/* Footer Actions */}
        <div
          style={{
            padding: '16px 20px',
            borderTop: '1px solid var(--ro-color-border, #e5e7eb)',
            display: 'flex',
            gap: '12px',
          }}
        >
          <button
            type="button"
            onClick={onCancel}
            style={{
              flex: 1,
              padding: '10px',
              borderRadius: '8px',
              border: '1px solid var(--ro-color-border, #e5e7eb)',
              backgroundColor: 'transparent',
              fontWeight: 500,
              cursor: 'pointer',
            }}
          >
            Vazgeç
          </button>
          <button
            type="button"
            onClick={handleSave}
            style={{
              flex: 2,
              padding: '10px',
              borderRadius: '8px',
              border: 'none',
              backgroundColor: 'var(--ro-color-primary, #2563eb)',
              color: '#ffffff',
              fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            {initialLine ? 'Güncelle' : 'Sepete Ekle'} (₺{(priceResult.lineTotalMinorUnits / 100).toFixed(2)})
          </button>
        </div>
      </div>
    </div>
  );
};
