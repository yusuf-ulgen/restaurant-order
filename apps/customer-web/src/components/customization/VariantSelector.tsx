import React from 'react';
import type { RuntimeItemVariant } from '@restaurant-order/contracts';
import { formatCurrency } from '../../utils/menuUtils';

export interface VariantSelectorProps {
  variants: RuntimeItemVariant[];
  selectedVariantId: string | null;
  onSelectVariant: (variantId: string) => void;
  currency: string;
  error?: string;
}

export const VariantSelector: React.FC<VariantSelectorProps> = ({
  variants,
  selectedVariantId,
  onSelectVariant,
  currency,
  error,
}) => {
  if (!variants || variants.length === 0) return null;

  return (
    <fieldset
      style={{
        border: 'none',
        padding: 0,
        margin: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--ro-space-2)',
      }}
      data-testid="variant-selector"
      aria-describedby={error ? 'variant-error-msg' : undefined}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          marginBottom: 'var(--ro-space-1)',
        }}
      >
        <legend
          style={{
            fontSize: 'var(--ro-font-size-sm)',
            fontWeight: 'var(--ro-font-weight-bold)',
            color: 'var(--ro-color-text-primary)',
            padding: 0,
          }}
        >
          Porsiyon / Seçenek Seçimi
        </legend>
        <span
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            color: 'var(--ro-color-text-muted)',
            fontWeight: 'var(--ro-font-weight-medium)',
          }}
        >
          Zorunlu
        </span>
      </div>

      {error && (
        <div
          id="variant-error-msg"
          role="alert"
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            color: 'var(--ro-color-danger, #ef4444)',
            marginBottom: 'var(--ro-space-1)',
          }}
        >
          {error}
        </div>
      )}

      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-2)' }}>
        {variants.map((v) => {
          const isSelected = selectedVariantId === v.id;
          const isOutOfStock = !v.isAvailable;

          return (
            <label
              key={v.id}
              data-testid={`variant-option-${v.id}`}
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: 'var(--ro-space-3)',
                borderRadius: 'var(--ro-radius-md)',
                border: '1px solid',
                borderColor: isSelected
                  ? 'var(--ro-color-text-primary)'
                  : 'var(--ro-color-border)',
                backgroundColor: isSelected
                  ? 'var(--ro-color-surface-hover)'
                  : 'var(--ro-color-surface)',
                cursor: isOutOfStock ? 'not-allowed' : 'pointer',
                opacity: isOutOfStock ? 0.6 : 1,
                minHeight: '44px',
                transition: 'all var(--ro-transition-fast)',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
                <input
                  type="radio"
                  name="item-variant"
                  value={v.id}
                  checked={isSelected}
                  disabled={isOutOfStock}
                  onChange={() => !isOutOfStock && onSelectVariant(v.id)}
                  style={{
                    cursor: isOutOfStock ? 'not-allowed' : 'pointer',
                    width: '18px',
                    height: '18px',
                  }}
                  aria-label={`${v.name} - ${formatCurrency(v.absolutePriceMinorUnits, currency)}${
                    isOutOfStock ? ' (Tükendi)' : ''
                  }`}
                />
                <span
                  style={{
                    fontSize: 'var(--ro-font-size-sm)',
                    fontWeight: isSelected
                      ? 'var(--ro-font-weight-semibold)'
                      : 'var(--ro-font-weight-regular)',
                    color: isOutOfStock
                      ? 'var(--ro-color-text-muted)'
                      : 'var(--ro-color-text-primary)',
                  }}
                >
                  {v.name}
                </span>
                {isOutOfStock && (
                  <span
                    style={{
                      fontSize: 'var(--ro-font-size-xs)',
                      color: 'var(--ro-color-danger, #ef4444)',
                      fontWeight: 'var(--ro-font-weight-medium)',
                    }}
                  >
                    (Tükendi)
                  </span>
                )}
              </div>

              <span
                style={{
                  fontSize: 'var(--ro-font-size-sm)',
                  fontWeight: 'var(--ro-font-weight-bold)',
                  color: isOutOfStock
                    ? 'var(--ro-color-text-muted)'
                    : 'var(--ro-color-text-primary)',
                }}
              >
                {formatCurrency(v.absolutePriceMinorUnits, currency)}
              </span>
            </label>
          );
        })}
      </div>
    </fieldset>
  );
};
