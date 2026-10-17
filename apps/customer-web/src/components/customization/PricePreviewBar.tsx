import React from 'react';
import { Button } from '@restaurant-order/ui';
import { formatCurrency } from '../../utils/menuUtils';

export interface PricePreviewBarProps {
  unitPriceMinorUnits: number;
  lineTotalMinorUnits: number;
  currency: string;
  quantity: number;
  isValid: boolean;
  isItemAvailable: boolean;
  onAddToCart: () => void;
}

export const PricePreviewBar: React.FC<PricePreviewBarProps> = ({
  unitPriceMinorUnits,
  lineTotalMinorUnits,
  currency,
  quantity,
  isValid,
  isItemAvailable,
  onAddToCart,
}) => {
  const isCtaDisabled = !isValid || !isItemAvailable;

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--ro-space-2)',
        paddingTop: 'var(--ro-space-3)',
        borderTop: '1px solid var(--ro-color-border)',
        marginTop: 'var(--ro-space-2)',
      }}
      data-testid="price-preview-bar"
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <div>
          <span
            style={{
              fontSize: 'var(--ro-font-size-xs)',
              color: 'var(--ro-color-text-muted)',
            }}
          >
            Birim Fiyat:
          </span>{' '}
          <span
            style={{
              fontSize: 'var(--ro-font-size-sm)',
              fontWeight: 'var(--ro-font-weight-medium)',
              color: 'var(--ro-color-text-primary)',
            }}
            data-testid="unit-price-display"
          >
            {formatCurrency(unitPriceMinorUnits, currency)}
          </span>
        </div>

        <div style={{ textAlign: 'right' }}>
          <span
            style={{
              fontSize: 'var(--ro-font-size-xs)',
              color: 'var(--ro-color-text-muted)',
            }}
          >
            Toplam ({quantity} adet):
          </span>{' '}
          <span
            style={{
              fontSize: 'var(--ro-font-size-lg)',
              fontWeight: 'var(--ro-font-weight-bold)',
              color: 'var(--ro-color-text-primary)',
            }}
            data-testid="line-total-display"
          >
            {formatCurrency(lineTotalMinorUnits, currency)}
          </span>
        </div>
      </div>

      <Button
        type="button"
        variant="primary"
        size="lg"
        disabled={isCtaDisabled}
        onClick={onAddToCart}
        data-testid="add-to-cart-cta-btn"
        style={{
          minHeight: '48px',
          width: '100%',
          display: 'flex',
          justifyContent: 'center',
          alignItems: 'center',
          gap: 'var(--ro-space-2)',
        }}
      >
        <span>Sepete Ekle</span>
        <span>•</span>
        <span>{formatCurrency(lineTotalMinorUnits, currency)}</span>
      </Button>

      <div
        style={{
          fontSize: '11px',
          color: 'var(--ro-color-text-muted)',
          textAlign: 'center',
          lineHeight: 1.3,
        }}
        data-testid="price-disclaimer-note"
      >
        * Görüntülenen tutar önizleme niteliğindedir; sipariş gönderiminde güncel sunucu fiyatı esas alınır.
      </div>
    </div>
  );
};
