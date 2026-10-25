import React, { useState, useEffect } from 'react';
import { Card } from '../components/Card.js';
import { Button } from '../components/Button.js';
import {
  isPushSupported,
  getPushPermission,
  isPushDismissed,
  dismissPushPrompt,
  requestPushPermission,
  getOrCreateInstallationId,
} from './pushNotifications.js';

export interface PushNotificationBannerProps {
  onPermissionGranted?: (installationId: string) => void | Promise<void>;
  className?: string;
}

export const PushNotificationBanner: React.FC<PushNotificationBannerProps> = ({
  onPermissionGranted,
  className,
}) => {
  const [isVisible, setIsVisible] = useState(false);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    if (!isPushSupported()) {
      setIsVisible(false);
      return;
    }

    const permission = getPushPermission();
    if (permission === 'default' && !isPushDismissed()) {
      setIsVisible(true);
    } else {
      setIsVisible(false);
    }
  }, []);

  if (!isVisible) {
    return null;
  }

  const handleEnable = async () => {
    setIsLoading(true);
    try {
      const result = await requestPushPermission();
      if (result === 'granted') {
        const installationId = getOrCreateInstallationId();
        if (onPermissionGranted) {
          await onPermissionGranted(installationId);
        }
        setIsVisible(false);
      } else {
        // User denied or dismissed prompt
        setIsVisible(false);
      }
    } finally {
      setIsLoading(false);
    }
  };

  const handleDismiss = () => {
    dismissPushPrompt();
    setIsVisible(false);
  };

  return (
    <Card
      padding="md"
      className={className}
      data-testid="push-notification-banner"
      style={{
        margin: '0.75rem 0',
        padding: '1rem',
        border: '1px solid var(--color-border-subtle, #e5e7eb)',
        backgroundColor: 'var(--color-surface-elevated, #f9fafb)',
        borderRadius: '0.5rem',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: '0.75rem' }}>
        <span style={{ fontSize: '1.25rem', lineHeight: 1 }} aria-hidden="true">
          🔔
        </span>
        <div style={{ flex: 1 }}>
          <h3
            style={{
              margin: '0 0 0.25rem 0',
              fontSize: '0.875rem',
              fontWeight: 600,
              color: 'var(--color-text-primary, #111827)',
            }}
          >
            Anlık Bildirimler
          </h3>
          <p
            style={{
              margin: 0,
              fontSize: '0.8125rem',
              color: 'var(--color-text-secondary, #4b5563)',
              lineHeight: 1.4,
            }}
          >
            Siparişinizin hazırlanma durumu ve servis çağrılarınızı anında takip etmek için bildirimleri açabilirsiniz.
          </p>
          <div style={{ marginTop: '0.75rem', display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
            <Button
              variant="primary"
              size="sm"
              onClick={handleEnable}
              disabled={isLoading}
              data-testid="enable-push-button"
            >
              {isLoading ? 'Açılıyor...' : 'Bildirimleri Aç'}
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={handleDismiss}
              disabled={isLoading}
              data-testid="dismiss-push-button"
            >
              Şimdi Değil
            </Button>
          </div>
        </div>
      </div>
    </Card>
  );
};
