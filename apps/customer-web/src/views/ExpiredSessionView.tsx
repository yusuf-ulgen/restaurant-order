import React from 'react';
import { Card, Button, EmptyState } from '@restaurant-order/ui';

export interface ExpiredSessionViewProps {
  onScanNewQr?: () => void;
}

export const ExpiredSessionView: React.FC<ExpiredSessionViewProps> = ({
  onScanNewQr,
}) => {
  return (
    <div style={{ maxWidth: '480px', margin: '0 auto', padding: 'var(--ro-space-4)' }} data-testid="expired-session-view">
      <Card padding="md">
        <EmptyState
          title="Oturum Sona Erdi"
          description="Bu masaya ait yemek oturumu kapatılmış veya süresi dolmuştur. Yeni bir oturum başlatmak için masadaki güncel QR kodu okutabilirsiniz."
        />
        {onScanNewQr && (
          <div style={{ marginTop: 'var(--ro-space-4)', display: 'flex', justifyContent: 'center' }}>
            <Button variant="primary" size="md" onClick={onScanNewQr}>
              Yeni QR Kod Okut
            </Button>
          </div>
        )}
      </Card>
    </div>
  );
};
