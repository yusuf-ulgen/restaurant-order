import React, { useState } from 'react';
import { Card, Button, Input, FormField } from '@restaurant-order/ui';

export interface UnauthenticatedQrViewProps {
  onTokenSubmit?: (token: string) => void;
}

export const UnauthenticatedQrView: React.FC<UnauthenticatedQrViewProps> = ({
  onTokenSubmit,
}) => {
  const [manualToken, setManualToken] = useState('');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (manualToken.trim() && onTokenSubmit) {
      onTokenSubmit(manualToken.trim());
    }
  };

  return (
    <div
      style={{ maxWidth: '480px', margin: '0 auto', padding: 'var(--ro-space-4)' }}
      data-testid="unauthenticated-qr-view"
    >
      <Card padding="md">
        <h2 className="customer-card-title">Masadaki QR Kodu Okutun</h2>
        <p className="customer-card-text">
          Sipariş vermek, menüyü incelemek ve masanıza servis çağırmak için masanızda bulunan
          QR kodu telefonunuzun kamerasıyla tarayın.
        </p>

        {onTokenSubmit && (
          <form onSubmit={handleSubmit} style={{ marginTop: 'var(--ro-space-4)' }}>
            <FormField label="veya Test QR Kodu Girin" id="manual-qr-token">
              <Input
                id="manual-qr-token"
                placeholder="Örn: test-qr-token"
                value={manualToken}
                onChange={(e) => setManualToken(e.target.value)}
              />
            </FormField>
            <div style={{ marginTop: 'var(--ro-space-3)' }}>
              <Button
                type="submit"
                variant="primary"
                size="md"
                disabled={!manualToken.trim()}
              >
                Giriş Yap
              </Button>
            </div>
          </form>
        )}
      </Card>
    </div>
  );
};
