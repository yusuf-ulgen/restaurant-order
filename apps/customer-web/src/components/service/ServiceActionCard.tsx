import React, { useState } from 'react';
import { Button, Badge, Spinner } from '@restaurant-order/ui';
import type { ServiceRequestDto, ServiceRequestType } from '@restaurant-order/contracts';

export interface ServiceActionConfig {
  key: ServiceRequestType | 'Bill';
  title: string;
  icon: string;
  description: string;
  confirmationMessage: string;
  allowNote?: boolean;
}

export interface ServiceActionCardProps {
  config: ServiceActionConfig;
  openRequest?: ServiceRequestDto;
  isBillRequested?: boolean;
  isSubmitting: boolean;
  isCancelling: boolean;
  cooldownSeconds: number;
  errorMessage?: string | null;
  onConfirm: (key: ServiceRequestType | 'Bill', note?: string) => void;
  onCancelRequest?: (requestId: string) => void;
  onRetry?: () => void;
}

export const ServiceActionCard: React.FC<ServiceActionCardProps> = ({
  config,
  openRequest,
  isBillRequested = false,
  isSubmitting,
  isCancelling,
  cooldownSeconds,
  errorMessage,
  onConfirm,
  onCancelRequest,
  onRetry,
}) => {
  const [isConfirming, setIsConfirming] = useState(false);
  const [note, setNote] = useState('');

  const isOpen = Boolean(openRequest);
  const isTerminalBill = config.key === 'Bill' && isBillRequested;
  const isActionDisabled = isSubmitting || isCancelling || cooldownSeconds > 0;

  const handleStartConfirm = () => {
    setIsConfirming(true);
  };

  const handleCancelConfirm = () => {
    setIsConfirming(false);
    setNote('');
  };

  const handleSubmit = () => {
    if (isActionDisabled) return;
    setIsConfirming(false);
    onConfirm(config.key, note.trim() || undefined);
    setNote('');
  };

  return (
    <div
      className="service-action-card"
      data-testid={`service-card-${config.key.toLowerCase()}`}
      style={{
        display: 'flex',
        flexDirection: 'column',
        padding: 'var(--ro-space-4)',
        backgroundColor: 'var(--ro-color-surface-elevated, #ffffff)',
        borderRadius: 'var(--ro-radius-md, 8px)',
        border: '1px solid var(--ro-color-border, #e5e7eb)',
        gap: 'var(--ro-space-3)',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 'var(--ro-space-2)' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
          <span style={{ fontSize: '24px' }} role="img" aria-hidden="true">
            {config.icon}
          </span>
          <div>
            <h3
              style={{
                margin: 0,
                fontSize: 'var(--ro-font-size-md, 16px)',
                fontWeight: 'var(--ro-font-weight-bold, 700)',
                color: 'var(--ro-color-text-primary)',
              }}
            >
              {config.title}
            </h3>
            <p
              style={{
                margin: '2px 0 0',
                fontSize: 'var(--ro-font-size-xs, 12px)',
                color: 'var(--ro-color-text-secondary)',
              }}
            >
              {config.description}
            </p>
          </div>
        </div>

        {isOpen && (
          <Badge variant="warning" data-testid={`badge-open-${config.key.toLowerCase()}`}>
            Talebiniz Alındı
          </Badge>
        )}

        {isTerminalBill && (
          <Badge variant="primary" data-testid="badge-bill-requested">
            Hesap İstendi
          </Badge>
        )}
      </div>

      {/* Error & Retry Banner */}
      {errorMessage && (
        <div
          role="alert"
          style={{
            padding: 'var(--ro-space-2) var(--ro-space-3)',
            backgroundColor: 'var(--ro-color-error-subtle)',
            color: 'var(--ro-color-error-text)',
            borderRadius: 'var(--ro-radius-sm)',
            fontSize: 'var(--ro-font-size-xs)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 'var(--ro-space-2)',
          }}
          data-testid={`error-banner-${config.key.toLowerCase()}`}
        >
          <span>{errorMessage}</span>
          {onRetry && (
            <Button
              variant="outline"
              size="sm"
              onClick={onRetry}
              disabled={isSubmitting}
              data-testid={`retry-btn-${config.key.toLowerCase()}`}
              style={{ minHeight: '32px' }}
            >
              Tekrar Dene
            </Button>
          )}
        </div>
      )}

      {/* Confirmation Step with Optional Note */}
      {isConfirming && !isOpen && !isTerminalBill && (
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--ro-space-2)',
            padding: 'var(--ro-space-3)',
            backgroundColor: 'var(--ro-color-surface)',
            borderRadius: 'var(--ro-radius-sm)',
            border: '1px dashed var(--ro-color-border)',
          }}
          data-testid={`confirm-box-${config.key.toLowerCase()}`}
        >
          <p
            style={{
              margin: 0,
              fontSize: 'var(--ro-font-size-sm)',
              fontWeight: 'var(--ro-font-weight-medium)',
              color: 'var(--ro-color-text-primary)',
            }}
          >
            {config.confirmationMessage}
          </p>

          {config.allowNote && (
            <div>
              <label
                htmlFor={`note-${config.key}`}
                style={{
                  display: 'block',
                  fontSize: 'var(--ro-font-size-xs)',
                  color: 'var(--ro-color-text-secondary)',
                  marginBottom: '4px',
                }}
              >
                İsteğe Bağlı Not (En fazla 200 karakter)
              </label>
              <input
                id={`note-${config.key}`}
                type="text"
                maxLength={200}
                value={note}
                onChange={(e) => setNote(e.target.value)}
                placeholder="Örn: 2 adet lütfen"
                style={{
                  width: '100%',
                  padding: '8px',
                  borderRadius: 'var(--ro-radius-sm)',
                  border: '1px solid var(--ro-color-border)',
                  fontSize: 'var(--ro-font-size-sm)',
                  boxSizing: 'border-box',
                }}
                data-testid={`note-input-${config.key.toLowerCase()}`}
              />
            </div>
          )}

          <div style={{ display: 'flex', gap: 'var(--ro-space-2)', marginTop: '4px' }}>
            <Button
              variant="primary"
              size="sm"
              onClick={handleSubmit}
              disabled={isActionDisabled}
              data-testid={`submit-confirm-btn-${config.key.toLowerCase()}`}
              style={{ minHeight: '44px', flex: 1 }}
            >
              {isSubmitting ? (
                <>
                  <Spinner size="sm" />
                  <span style={{ marginLeft: '6px' }}>Gönderiliyor...</span>
                </>
              ) : (
                'Evet, İlet'
              )}
            </Button>
            <Button
              variant="secondary"
              size="sm"
              onClick={handleCancelConfirm}
              disabled={isSubmitting}
              data-testid={`cancel-confirm-btn-${config.key.toLowerCase()}`}
              style={{ minHeight: '44px' }}
            >
              Vazgeç
            </Button>
          </div>
        </div>
      )}

      {/* Main Actions / States */}
      {!isConfirming && (
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--ro-space-2)' }}>
          {isOpen && openRequest && (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', width: '100%', gap: '8px' }}>
              <span
                style={{
                  fontSize: 'var(--ro-font-size-xs)',
                  color: 'var(--ro-color-text-secondary)',
                  fontStyle: 'italic',
                }}
              >
                {openRequest.note ? `Not: ${openRequest.note}` : 'Talebiniz personele iletildi.'}
              </span>
              {onCancelRequest && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => onCancelRequest(openRequest.id)}
                  disabled={isCancelling}
                  data-testid={`cancel-request-btn-${config.key.toLowerCase()}`}
                  style={{ minHeight: '44px', minWidth: '90px' }}
                >
                  {isCancelling ? <Spinner size="sm" /> : 'İptal Et'}
                </Button>
              )}
            </div>
          )}

          {isTerminalBill && (
            <span
              style={{
                fontSize: 'var(--ro-font-size-xs)',
                color: 'var(--ro-color-text-secondary)',
              }}
            >
              Hesap talebiniz alınmıştır. Görevli personel masanıza gelecektir.
            </span>
          )}

          {!isOpen && !isTerminalBill && (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', width: '100%' }}>
              {cooldownSeconds > 0 ? (
                <span
                  style={{
                    fontSize: 'var(--ro-font-size-xs)',
                    color: 'var(--ro-color-text-muted)',
                  }}
                  data-testid={`cooldown-text-${config.key.toLowerCase()}`}
                >
                  Lütfen bekleyin: {cooldownSeconds} sn
                </span>
              ) : (
                <span />
              )}
              <Button
                variant={config.key === 'Bill' ? 'primary' : 'secondary'}
                size="md"
                onClick={handleStartConfirm}
                disabled={isActionDisabled}
                data-testid={`action-btn-${config.key.toLowerCase()}`}
                style={{ minHeight: '44px', minWidth: '120px' }}
              >
                {isSubmitting ? (
                  <>
                    <Spinner size="sm" />
                    <span style={{ marginLeft: '6px' }}>Gönderiliyor...</span>
                  </>
                ) : (
                  config.title
                )}
              </Button>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
