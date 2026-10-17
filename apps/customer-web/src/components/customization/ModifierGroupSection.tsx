import React from 'react';
import type { RuntimeModifierGroup } from '@restaurant-order/contracts';
import { formatCurrency } from '../../utils/menuUtils';

export interface ModifierGroupSectionProps {
  group: RuntimeModifierGroup;
  selectedOptionIds: string[];
  onToggleOption: (groupId: string, optionId: string) => void;
  currency: string;
  error?: string;
}

export const ModifierGroupSection: React.FC<ModifierGroupSectionProps> = ({
  group,
  selectedOptionIds,
  onToggleOption,
  currency,
  error,
}) => {
  const isRequired = group.minSelections > 0;
  const isSingleChoice = group.maxSelections === 1;

  let boundLabel = 'İsteğe bağlı';
  if (isRequired) {
    if (group.minSelections === group.maxSelections) {
      boundLabel = `Zorunlu (${group.minSelections} seçim)`;
    } else {
      boundLabel = `Zorunlu (En az ${group.minSelections}, en fazla ${group.maxSelections} seçim)`;
    }
  } else if (group.maxSelections > 1) {
    boundLabel = `İsteğe bağlı (En fazla ${group.maxSelections} seçim)`;
  }

  const errorId = `modifier-group-error-${group.id}`;

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
      data-testid={`modifier-group-${group.id}`}
      aria-describedby={error ? errorId : undefined}
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
          {group.name}
        </legend>
        <span
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            color: isRequired ? 'var(--ro-color-text-primary)' : 'var(--ro-color-text-muted)',
            fontWeight: isRequired
              ? 'var(--ro-font-weight-semibold)'
              : 'var(--ro-font-weight-regular)',
          }}
        >
          {boundLabel}
        </span>
      </div>

      {error && (
        <div
          id={errorId}
          role="alert"
          data-testid={`modifier-error-${group.id}`}
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
        {(group.options || []).map((option) => {
          const isSelected = selectedOptionIds.includes(option.id);
          const priceDelta = option.priceDeltaMinorUnits;

          let deltaLabel = 'Ücretsiz';
          if (priceDelta > 0) {
            deltaLabel = `+${formatCurrency(priceDelta, currency)}`;
          } else if (priceDelta < 0) {
            deltaLabel = `-${formatCurrency(Math.abs(priceDelta), currency)}`;
          }

          return (
            <label
              key={option.id}
              data-testid={`modifier-option-${option.id}`}
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
                cursor: 'pointer',
                minHeight: '44px',
                transition: 'all var(--ro-transition-fast)',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
                <input
                  type={isSingleChoice ? 'radio' : 'checkbox'}
                  name={`group-${group.id}`}
                  value={option.id}
                  checked={isSelected}
                  onChange={() => onToggleOption(group.id, option.id)}
                  style={{
                    cursor: 'pointer',
                    width: '18px',
                    height: '18px',
                  }}
                  aria-label={`${option.name} - ${deltaLabel}`}
                />
                <span
                  style={{
                    fontSize: 'var(--ro-font-size-sm)',
                    fontWeight: isSelected
                      ? 'var(--ro-font-weight-semibold)'
                      : 'var(--ro-font-weight-regular)',
                    color: 'var(--ro-color-text-primary)',
                  }}
                >
                  {option.name}
                </span>
              </div>

              <span
                style={{
                  fontSize: 'var(--ro-font-size-xs)',
                  fontWeight: priceDelta !== 0
                    ? 'var(--ro-font-weight-bold)'
                    : 'var(--ro-font-weight-regular)',
                  color: priceDelta > 0
                    ? 'var(--ro-color-text-primary)'
                    : 'var(--ro-color-text-muted)',
                }}
              >
                {deltaLabel}
              </span>
            </label>
          );
        })}
      </div>
    </fieldset>
  );
};
