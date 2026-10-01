import React from 'react';
import { Card, Button, Badge } from '@restaurant-order/ui';
import { TerminalContextDto } from '@restaurant-order/contracts';
import { PinPad } from './PinPad';
import { useTerminal } from './TerminalContext';

interface PinPadViewProps {
  terminal: TerminalContextDto;
}

export const PinPadView: React.FC<PinPadViewProps> = ({ terminal }) => {
  const { loginWithPin, deactivateTerminal, isLoading, error } = useTerminal();

  const handlePinSubmit = async (pin: string) => {
    await loginWithPin(pin);
  };

  return (
    <div
      style={{
        display: 'flex',
        minHeight: '100vh',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '1.5rem',
        backgroundColor: 'var(--color-bg, #f8fafc)',
      }}
    >
      <Card padding="lg" style={{ width: '100%', maxWidth: '380px', textAlign: 'center' }}>
        <div style={{ marginBottom: '1rem' }}>
          <div style={{ display: 'flex', justifyContent: 'center', gap: '0.5rem', marginBottom: '0.5rem' }}>
            <Badge variant="primary">{terminal.terminalName}</Badge>
            <Badge variant="neutral">Aktif Terminal</Badge>
          </div>
          <h2 style={{ fontSize: '1.25rem', fontWeight: 700, margin: '0 0 0.25rem 0' }}>
            Terminal Kilitli
          </h2>
          <p style={{ color: 'var(--color-text-muted, #64748b)', fontSize: '0.875rem', margin: 0 }}>
            İşlem yapmak için 4 haneli personel PIN kodunuzu girin.
          </p>
        </div>

        <PinPad
          onSubmit={handlePinSubmit}
          isLoading={isLoading}
          errorMessage={error}
        />

        <div style={{ marginTop: '1.5rem', borderTop: '1px solid #e2e8f0', paddingTop: '1rem' }}>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={deactivateTerminal}
            disabled={isLoading}
            style={{ color: '#64748b', fontSize: '0.75rem' }}
          >
            Farklı Terminal / Cihazı Sıfırla
          </Button>
        </div>
      </Card>
    </div>
  );
};
