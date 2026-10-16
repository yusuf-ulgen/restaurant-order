import React from 'react';

export interface QuantitySelectorProps {
  quantity: number;
  onChangeQuantity: (quantity: number) => void;
  min?: number;
  max?: number;
}

export const QuantitySelector: React.FC<QuantitySelectorProps> = ({
  quantity,
  onChangeQuantity,
  min = 1,
  max = 99,
}) => {
  const handleDecrement = () => {
    if (quantity > min) {
      onChangeQuantity(quantity - 1);
    }
  };

  const handleIncrement = () => {
    if (quantity < max) {
      onChangeQuantity(quantity + 1);
    }
  };

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: 'var(--ro-space-2) 0',
      }}
      data-testid="quantity-selector"
    >
      <div>
        <div
          style={{
            fontSize: 'var(--ro-font-size-sm)',
            fontWeight: 'var(--ro-font-weight-bold)',
            color: 'var(--ro-color-text-primary)',
          }}
        >
          Adet / Miktar
        </div>
        <div
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            color: 'var(--ro-color-text-muted)',
          }}
        >
          Sipariş adedini belirleyin
        </div>
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--ro-space-3)',
          backgroundColor: 'var(--ro-color-surface-hover)',
          borderRadius: 'var(--ro-radius-full)',
          padding: '4px',
          border: '1px solid var(--ro-color-border)',
        }}
      >
        <button
          type="button"
          onClick={handleDecrement}
          disabled={quantity <= min}
          aria-label="Miktarı azalt"
          data-testid="quantity-decrement-btn"
          style={{
            width: '44px',
            height: '44px',
            borderRadius: 'var(--ro-radius-full)',
            border: 'none',
            backgroundColor: quantity <= min ? 'transparent' : 'var(--ro-color-surface)',
            color: quantity <= min ? 'var(--ro-color-text-muted)' : 'var(--ro-color-text-primary)',
            fontSize: 'var(--ro-font-size-lg)',
            fontWeight: 'var(--ro-font-weight-bold)',
            cursor: quantity <= min ? 'not-allowed' : 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            transition: 'all var(--ro-transition-fast)',
            boxShadow: quantity > min ? 'var(--ro-shadow-xs, 0 1px 2px rgba(0,0,0,0.05))' : 'none',
          }}
        >
          −
        </button>

        <span
          data-testid="quantity-display"
          role="status"
          aria-live="polite"
          style={{
            fontSize: 'var(--ro-font-size-md)',
            fontWeight: 'var(--ro-font-weight-bold)',
            color: 'var(--ro-color-text-primary)',
            minWidth: '24px',
            textAlign: 'center',
          }}
        >
          {quantity}
        </span>

        <button
          type="button"
          onClick={handleIncrement}
          disabled={quantity >= max}
          aria-label="Miktarı artır"
          data-testid="quantity-increment-btn"
          style={{
            width: '44px',
            height: '44px',
            borderRadius: 'var(--ro-radius-full)',
            border: 'none',
            backgroundColor: quantity >= max ? 'transparent' : 'var(--ro-color-surface)',
            color: quantity >= max ? 'var(--ro-color-text-muted)' : 'var(--ro-color-text-primary)',
            fontSize: 'var(--ro-font-size-lg)',
            fontWeight: 'var(--ro-font-weight-bold)',
            cursor: quantity >= max ? 'not-allowed' : 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            transition: 'all var(--ro-transition-fast)',
            boxShadow: quantity < max ? 'var(--ro-shadow-xs, 0 1px 2px rgba(0,0,0,0.05))' : 'none',
          }}
        >
          +
        </button>
      </div>
    </div>
  );
};
