import React from 'react';
import type { CustomerOrderResponseDto } from '@restaurant-order/contracts';
import { BottomSheet, Button } from '@restaurant-order/ui';
import { formatCurrency } from '../../utils/menuUtils';

export interface OrderSuccessSheetProps {
  isOpen: boolean;
  onClose: () => void;
  order: CustomerOrderResponseDto | null;
  onViewOrders?: () => void;
  onBrowseMenu?: () => void;
}

export const OrderSuccessSheet: React.FC<OrderSuccessSheetProps> = ({
  isOpen,
  onClose,
  order,
  onViewOrders,
  onBrowseMenu,
}) => {
  if (!order) return null;

  return (
    <BottomSheet
      isOpen={isOpen}
      onClose={onClose}
      title={
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
          <span role="img" aria-label="Başarılı">
            ✅
          </span>
          <span>Siparişiniz Alındı!</span>
        </div>
      }
      description={`Tur #${order.roundNumber} mutfağa başarıyla iletildi.`}
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--ro-space-4)',
          boxSizing: 'border-box',
        }}
        data-testid="order-success-sheet"
      >
        {/* Status Badge & Round summary banner */}
        <div
          style={{
            padding: 'var(--ro-space-4)',
            backgroundColor: 'var(--ro-color-surface-subtle, #f9fafb)',
            borderRadius: 'var(--ro-radius-md)',
            border: '1px solid var(--ro-color-border)',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--ro-space-2)',
            alignItems: 'center',
            textAlign: 'center',
          }}
          data-testid="order-success-banner"
        >
          <div
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              padding: '4px 12px',
              borderRadius: 'var(--ro-radius-full)',
              backgroundColor: '#dbeafe',
              color: '#1e40af',
              fontSize: 'var(--ro-font-size-xs)',
              fontWeight: 'var(--ro-font-weight-bold)',
            }}
            data-testid="order-status-badge"
          >
            Durum: Mutfak Onayı Bekliyor (Submitted)
          </div>

          <div
            style={{
              fontSize: 'var(--ro-font-size-lg)',
              fontWeight: 'var(--ro-font-weight-bold)',
              color: 'var(--ro-color-text-primary)',
            }}
            data-testid="order-round-number"
          >
            Sipariş Turu #{order.roundNumber}
          </div>

          <div style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
            Hazırlık sırasına eklendi. Masanıza en kısa sürede servis edilecektir.
          </div>
        </div>

        {/* Order items recap */}
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--ro-space-2)',
            maxHeight: '220px',
            overflowY: 'auto',
          }}
          data-testid="order-success-items-list"
        >
          <div
            style={{
              fontSize: 'var(--ro-font-size-xs)',
              fontWeight: 'var(--ro-font-weight-bold)',
              color: 'var(--ro-color-text-secondary)',
              textTransform: 'uppercase',
              letterSpacing: '0.5px',
            }}
          >
            Gönderilen Ürünler
          </div>

          {order.items.map((item) => (
            <div
              key={item.id}
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                fontSize: 'var(--ro-font-size-sm)',
                padding: 'var(--ro-space-2) 0',
                borderBottom: '1px solid var(--ro-color-border-subtle, #f3f4f6)',
              }}
            >
              <div>
                <span style={{ fontWeight: 'var(--ro-font-weight-medium)' }}>
                  {item.quantity}x {item.itemName}
                </span>
                {item.variantName && (
                  <span style={{ color: 'var(--ro-color-text-secondary)', marginLeft: '4px' }}>
                    ({item.variantName})
                  </span>
                )}
                {item.modifiers.length > 0 && (
                  <div style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
                    + {item.modifiers.map((m) => m.modifierOptionName).join(', ')}
                  </div>
                )}
              </div>
              <span style={{ fontWeight: 'var(--ro-font-weight-medium)', whiteSpace: 'nowrap' }}>
                {formatCurrency(item.lineTotalMinorUnits, order.currency)}
              </span>
            </div>
          ))}
        </div>

        {/* Total summary */}
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            paddingTop: 'var(--ro-space-2)',
            borderTop: '1px solid var(--ro-color-border)',
          }}
        >
          <span style={{ fontSize: 'var(--ro-font-size-sm)', color: 'var(--ro-color-text-secondary)' }}>
            Toplam Tutar
          </span>
          <span
            style={{
              fontSize: 'var(--ro-font-size-base)',
              fontWeight: 'var(--ro-font-weight-bold)',
              color: 'var(--ro-color-text-primary)',
            }}
            data-testid="order-success-total"
          >
            {formatCurrency(order.totalMinorUnits, order.currency)}
          </span>
        </div>

        {/* Action buttons */}
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--ro-space-2)',
            marginTop: 'var(--ro-space-2)',
          }}
        >
          {onViewOrders && (
            <Button
              type="button"
              variant="primary"
              size="lg"
              onClick={() => {
                onClose();
                onViewOrders();
              }}
              data-testid="view-orders-history-btn"
              style={{ minHeight: '48px', width: '100%' }}
            >
              Masa Siparişlerimi Gör
            </Button>
          )}

          <Button
            type="button"
            variant="secondary"
            size="md"
            onClick={() => {
              onClose();
              onBrowseMenu?.();
            }}
            data-testid="return-to-menu-btn"
            style={{ minHeight: '44px', width: '100%' }}
          >
            Menüyü İncelemeye Devam Et
          </Button>
        </div>
      </div>
    </BottomSheet>
  );
};
