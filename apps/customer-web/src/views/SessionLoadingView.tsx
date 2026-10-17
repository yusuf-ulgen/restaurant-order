import React from 'react';
import { Card, Spinner } from '@restaurant-order/ui';

export interface SessionLoadingViewProps {
  message?: string;
}

export const SessionLoadingView: React.FC<SessionLoadingViewProps> = ({
  message = 'Oturum bilgileri doğrulanıyor...',
}) => {
  return (
    <div style={{ maxWidth: '480px', margin: '0 auto', padding: 'var(--ro-space-6)' }} data-testid="session-loading-view">
      <Card padding="lg">
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 'var(--ro-space-4)', textAlign: 'center' }}>
          <Spinner size="lg" label="Yükleniyor" />
          <p style={{ margin: 0, color: 'var(--ro-color-text-muted)' }}>{message}</p>
        </div>
      </Card>
    </div>
  );
};
