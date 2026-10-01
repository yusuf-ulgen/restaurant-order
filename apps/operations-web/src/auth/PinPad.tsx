import React, { useState, useEffect } from 'react';
import { Button } from '@restaurant-order/ui';

interface PinPadProps {
  onSubmit: (pin: string) => Promise<void>;
  isLoading?: boolean;
  errorMessage?: string | null;
  onCancel?: () => void;
}

export const PinPad: React.FC<PinPadProps> = ({
  onSubmit,
  isLoading = false,
  errorMessage = null,
  onCancel,
}) => {
  const [pin, setPin] = useState('');

  const handleDigit = (digit: string) => {
    if (pin.length < 4 && !isLoading) {
      setPin((prev) => prev + digit);
    }
  };

  const handleDelete = () => {
    if (!isLoading) {
      setPin((prev) => prev.slice(0, -1));
    }
  };

  const handleClear = () => {
    if (!isLoading) {
      setPin('');
    }
  };

  useEffect(() => {
    if (pin.length === 4) {
      onSubmit(pin).then(() => {
        setPin('');
      });
    }
  }, [pin, onSubmit]);

  const digits = ['1', '2', '3', '4', '5', '6', '7', '8', '9'];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%', maxWidth: '320px', margin: '0 auto' }}>
      <h3 style={{ margin: '0 0 0.5rem 0', fontSize: '1.25rem', fontWeight: 600 }}>Personel PIN Girişi</h3>
      <p style={{ margin: '0 0 1.5rem 0', color: 'var(--color-text-muted, #64748b)', fontSize: '0.875rem' }}>
        4 haneli hızlı geçiş kodunuzu tuşlayın.
      </p>

      {/* 4-dot Visual Indicator */}
      <div style={{ display: 'flex', gap: '1rem', marginBottom: '1.5rem' }}>
        {[0, 1, 2, 3].map((index) => (
          <div
            key={index}
            style={{
              width: '18px',
              height: '18px',
              borderRadius: '50%',
              backgroundColor: index < pin.length ? 'var(--color-primary, #2563eb)' : '#e2e8f0',
              border: '2px solid',
              borderColor: index < pin.length ? 'var(--color-primary, #2563eb)' : '#cbd5e1',
              transition: 'all 0.15s ease-in-out',
            }}
          />
        ))}
      </div>

      {errorMessage && (
        <div
          role="alert"
          style={{
            padding: '0.5rem 0.75rem',
            marginBottom: '1rem',
            backgroundColor: '#fee2e2',
            color: '#991b1b',
            borderRadius: '0.375rem',
            fontSize: '0.875rem',
            textAlign: 'center',
            width: '100%',
          }}
        >
          {errorMessage}
        </div>
      )}

      {/* Numeric Keypad Grid */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(3, 1fr)',
          gap: '0.75rem',
          width: '100%',
          marginBottom: '1rem',
        }}
      >
        {digits.map((d) => (
          <Button
            key={d}
            type="button"
            variant="outline"
            size="lg"
            onClick={() => handleDigit(d)}
            disabled={isLoading || pin.length >= 4}
            style={{ fontSize: '1.25rem', height: '56px', fontWeight: 600 }}
          >
            {d}
          </Button>
        ))}

        <Button
          type="button"
          variant="outline"
          size="lg"
          onClick={handleClear}
          disabled={isLoading || pin.length === 0}
          style={{ fontSize: '0.875rem', height: '56px' }}
        >
          Temizle
        </Button>

        <Button
          type="button"
          variant="outline"
          size="lg"
          onClick={() => handleDigit('0')}
          disabled={isLoading || pin.length >= 4}
          style={{ fontSize: '1.25rem', height: '56px', fontWeight: 600 }}
        >
          0
        </Button>

        <Button
          type="button"
          variant="outline"
          size="lg"
          onClick={handleDelete}
          disabled={isLoading || pin.length === 0}
          style={{ fontSize: '1.25rem', height: '56px' }}
          aria-label="Sil"
        >
          ⌫
        </Button>
      </div>

      {onCancel && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onCancel}
          disabled={isLoading}
          style={{ width: '100%' }}
        >
          İptal
        </Button>
      )}
    </div>
  );
};
