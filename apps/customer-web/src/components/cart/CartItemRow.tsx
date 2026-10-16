import React, { useState } from 'react';
import type { DraftCartLine, CartValidationIssue } from '@restaurant-order/contracts';
import { formatCurrency } from '../../utils/menuUtils';

export interface CartItemRowProps {
  line: DraftCartLine;
  issue?: CartValidationIssue;
  onUpdateQuantity: (signature: string, quantity: number) => void;
  onRemoveLine: (signature: string) => void;
  currency: string;
}

export const CartItemRow: React.FC<CartItemRowProps> = ({
  line,
  issue,
  onUpdateQuantity,
  onRemoveLine,
  currency,
}) => {
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);

  const isBlocking =
    issue &&
    (issue.type === 'item_removed' ||
      issue.type === 'item_unavailable' ||
      issue.type === 'variant_removed' ||
      issue.type === 'variant_unavailable' ||
      issue.type === 'modifier_removed');

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--ro-space-2)',
        padding: 'var(--ro-space-3)',
        borderRadius: 'var(--ro-radius-md)',
        border: '1px solid',
        borderColor: isBlocking
          ? 'var(--ro-color-error-border, #fecaca)'
          : 'var(--ro-color-border)',
        backgroundColor: isBlocking
          ? 'var(--ro-color-error-subtle, #fef2f2)'
          : 'var(--ro-color-surface)',
        transition: 'all var(--ro-transition-fast)',
      }}
      data-testid={`cart-item-row-${line.configurationSignature}`}
    >
      {/* Header: Item Name and Line Total */}
      <div
        style={{
          display: 'flex',
          alignItems: 'baseline',
          justifyContent: 'space-between',
        }}
      >
        <div style={{ flex: 1, paddingRight: 'var(--ro-space-2)' }}>
          <span
            style={{
              fontSize: 'var(--ro-font-size-sm)',
              fontWeight: 'var(--ro-font-weight-bold)',
              color: 'var(--ro-color-text-primary)',
            }}
          >
            {line.menuItemId}
          </span>
          {line.variantId && (
            <span
              style={{
                marginLeft: 'var(--ro-space-1)',
                fontSize: 'var(--ro-font-size-xs)',
                color: 'var(--ro-color-text-muted)',
              }}
            >
              ({line.variantId})
            </span>
          )}
        </div>

        <span
          style={{
            fontSize: 'var(--ro-font-size-sm)',
            fontWeight: 'var(--ro-font-weight-bold)',
            color: 'var(--ro-color-text-primary)',
          }}
          data-testid={`line-total-${line.configurationSignature}`}
        >
          {formatCurrency(line.lineTotalMinorUnits, currency)}
        </span>
      </div>

      {/* Selected modifiers list if any */}
      {line.selectedModifierOptionIds.length > 0 && (
        <div
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            color: 'var(--ro-color-text-secondary)',
          }}
        >
          {line.selectedModifierOptionIds.join(', ')}
        </div>
      )}

      {/* Customer Note if present */}
      {line.customerNote && (
        <div
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            color: 'var(--ro-color-text-muted)',
            fontStyle: 'italic',
            backgroundColor: 'var(--ro-color-surface-hover)',
            padding: '4px 8px',
            borderRadius: 'var(--ro-radius-sm)',
          }}
          data-testid={`cart-item-note-${line.configurationSignature}`}
        >
          Not: {line.customerNote}
        </div>
      )}

      {/* Warning/Issue Banner */}
      {issue && (
        <div
          role="alert"
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            color: isBlocking ? 'var(--ro-color-error-text)' : 'var(--ro-color-warning-text, #92400e)',
            fontWeight: 'var(--ro-font-weight-medium)',
          }}
          data-testid={`cart-issue-${line.configurationSignature}`}
        >
          ⚠️ {issue.message}
          {issue.previousUnitPriceMinorUnits !== undefined &&
            issue.currentUnitPriceMinorUnits !== undefined && (
              <span style={{ marginLeft: '4px' }}>
                ({formatCurrency(issue.previousUnitPriceMinorUnits, currency)} →{' '}
                {formatCurrency(issue.currentUnitPriceMinorUnits, currency)})
              </span>
            )}
        </div>
      )}

      {/* Controls: Stepper and Delete */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          marginTop: 'var(--ro-space-1)',
        }}
      >
        {/* Quantity Stepper */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 'var(--ro-space-2)',
            backgroundColor: 'var(--ro-color-surface-hover)',
            borderRadius: 'var(--ro-radius-full)',
            padding: '2px 4px',
            border: '1px solid var(--ro-color-border)',
          }}
        >
          <button
            type="button"
            onClick={() => onUpdateQuantity(line.configurationSignature, line.quantity - 1)}
            aria-label="Adedi azalt"
            data-testid={`cart-decrement-${line.configurationSignature}`}
            style={{
              width: '36px',
              height: '36px',
              borderRadius: 'var(--ro-radius-full)',
              border: 'none',
              background: 'transparent',
              color: 'var(--ro-color-text-primary)',
              cursor: 'pointer',
              fontWeight: 'var(--ro-font-weight-bold)',
            }}
          >
            −
          </button>

          <span
            style={{
              fontSize: 'var(--ro-font-size-sm)',
              fontWeight: 'var(--ro-font-weight-bold)',
              minWidth: '20px',
              textAlign: 'center',
            }}
            data-testid={`cart-qty-${line.configurationSignature}`}
          >
            {line.quantity}
          </span>

          <button
            type="button"
            onClick={() => onUpdateQuantity(line.configurationSignature, line.quantity + 1)}
            disabled={line.quantity >= 99}
            aria-label="Adedi artır"
            data-testid={`cart-increment-${line.configurationSignature}`}
            style={{
              width: '36px',
              height: '36px',
              borderRadius: 'var(--ro-radius-full)',
              border: 'none',
              background: 'transparent',
              color: line.quantity >= 99 ? 'var(--ro-color-text-muted)' : 'var(--ro-color-text-primary)',
              cursor: line.quantity >= 99 ? 'not-allowed' : 'pointer',
              fontWeight: 'var(--ro-font-weight-bold)',
            }}
          >
            +
          </button>
        </div>

        {/* Delete Confirmation or Trigger */}
        {showDeleteConfirm ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-1)' }}>
            <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-error-text)' }}>
              Silinsin mi?
            </span>
            <button
              type="button"
              onClick={() => onRemoveLine(line.configurationSignature)}
              data-testid={`confirm-delete-${line.configurationSignature}`}
              style={{
                backgroundColor: 'var(--ro-color-error, #dc2626)',
                color: 'var(--ro-color-white)',
                border: 'none',
                borderRadius: 'var(--ro-radius-sm)',
                padding: '4px 8px',
                fontSize: 'var(--ro-font-size-xs)',
                cursor: 'pointer',
                minHeight: '36px',
              }}
            >
              Evet
            </button>
            <button
              type="button"
              onClick={() => setShowDeleteConfirm(false)}
              data-testid={`cancel-delete-${line.configurationSignature}`}
              style={{
                backgroundColor: 'transparent',
                color: 'var(--ro-color-text-muted)',
                border: '1px solid var(--ro-color-border)',
                borderRadius: 'var(--ro-radius-sm)',
                padding: '4px 8px',
                fontSize: 'var(--ro-font-size-xs)',
                cursor: 'pointer',
                minHeight: '36px',
              }}
            >
              Vazgeç
            </button>
          </div>
        ) : (
          <button
            type="button"
            onClick={() => setShowDeleteConfirm(true)}
            aria-label={`${line.menuItemId} ürününü sepetten çıkar`}
            data-testid={`remove-line-${line.configurationSignature}`}
            style={{
              background: 'transparent',
              border: 'none',
              color: 'var(--ro-color-text-muted)',
              fontSize: 'var(--ro-font-size-xs)',
              cursor: 'pointer',
              padding: 'var(--ro-space-1)',
              minHeight: '44px',
              display: 'flex',
              alignItems: 'center',
            }}
          >
            🗑️ Kaldır
          </button>
        )}
      </div>
    </div>
  );
};
