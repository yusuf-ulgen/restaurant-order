import React, { useState } from 'react';
import { Card, Button, BottomSheet } from '@restaurant-order/ui';
import type { CustomerSessionDto } from '@restaurant-order/contracts';

export interface ActiveSessionViewProps {
  session: CustomerSessionDto;
  onLeaveSession?: () => void;
}

export const ActiveSessionView: React.FC<ActiveSessionViewProps> = ({
  session,
}) => {
  const [isServiceSheetOpen, setIsServiceSheetOpen] = useState(false);
  const handleCloseServiceSheet = () => setIsServiceSheetOpen(false);

  return (
    <div style={{ maxWidth: '480px', margin: '0 auto', padding: 'var(--ro-space-4)' }} data-testid="active-session-view">
      <Card padding="md">
        <h2 className="customer-card-title">Hoş Geldiniz</h2>
        <p className="customer-card-text">
          <strong>Masa {session.tableNumber} ({session.tableName})</strong> için oturumunuz aktif.
          Menüyü inceleyebilir, ürünleri özelleştirebilir ve servis talebinde bulunabilirsiniz.
        </p>
        <div className="customer-actions" style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}>
          <Button
            variant="primary"
            size="md"
            className="customer-action-btn-primary"
            disabled
            title="Duyarlı menü taraması ve filtreleme Faz 7.1 kapsamında etkinleşecektir"
          >
            Menüyü İncele (Faz 7.1)
          </Button>
          <Button
            variant="outline"
            size="md"
            onClick={() => setIsServiceSheetOpen(true)}
          >
            Garson Çağır
          </Button>
        </div>
      </Card>

      {/* Service Request Bottom Sheet */}
      <BottomSheet
        isOpen={isServiceSheetOpen}
        onClose={handleCloseServiceSheet}
        title="Garson Çağır"
        description="Masanız için servis veya destek talebi iletin."
      >
        <p
          style={{
            fontSize: 'var(--ro-font-size-sm)',
            color: 'var(--ro-color-text-muted)',
            marginTop: 0,
            marginBottom: 'var(--ro-space-3)',
          }}
        >
          Servis çağrı iletimi Faz 7.4 (Servis Talepleri) kapsamında etkinleşecektir.
        </p>
        <div className="service-options-list" style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-2)' }}>
          <button
            type="button"
            className="service-option-item"
            disabled
            aria-disabled="true"
            title="Su ve peçete talebi Faz 7.4'te etkinleşecektir"
          >
            <span>Masaya Su / Peçete Talebi (Yakında)</span>
            <span aria-hidden="true">→</span>
          </button>
          <button
            type="button"
            className="service-option-item"
            disabled
            aria-disabled="true"
            title="Hesap talebi Faz 7.4'te etkinleşecektir"
          >
            <span>Hesap İstiyorum (Yakında)</span>
            <span aria-hidden="true">→</span>
          </button>
          <button
            type="button"
            className="service-option-item"
            disabled
            aria-disabled="true"
            title="Garson görüşme talebi Faz 7.4'te etkinleşecektir"
          >
            <span>Garson ile Görüşme Talebi (Yakında)</span>
            <span aria-hidden="true">→</span>
          </button>
        </div>
      </BottomSheet>
    </div>
  );
};
