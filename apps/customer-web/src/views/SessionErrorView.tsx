import React from 'react';
import { Card, Button, ErrorState } from '@restaurant-order/ui';

export interface SessionErrorViewProps {
  title?: string;
  message?: string;
  retryAfterSeconds?: number;
  onRetry?: () => void;
}

export const SessionErrorView: React.FC<SessionErrorViewProps> = ({
  title = 'Bağlantı Hatası',
  message = 'Oturum bilgileri alınırken bir hata oluştu. Lütfen tekrar deneyin.',
  retryAfterSeconds,
  onRetry,
}) => {
  return (
    <div style={{ maxWidth: '480px', margin: '0 auto', padding: 'var(--ro-space-4)' }} data-testid="session-error-view">
      <Card padding="md">
        <ErrorState
          title={title}
          message={message}
        />
        {retryAfterSeconds && (
          <p style={{ textAlign: 'center', color: 'var(--ro-color-text-muted)', fontSize: 'var(--ro-font-size-sm)' }}>
            Lütfen {retryAfterSeconds} saniye bekleyin.
          </p>
        )}
        {onRetry && (
          <div style={{ marginTop: 'var(--ro-space-4)', display: 'flex', justifyContent: 'center' }}>
            <Button variant="primary" size="md" onClick={onRetry}>
              Yeniden Dene
            </Button>
          </div>
        )}
      </Card>
    </div>
  );
};
