import React, { useState, useEffect, useCallback } from 'react';
import type { CustomerOrderResponseDto } from '@restaurant-order/contracts';
import { BottomSheet, Button } from '@restaurant-order/ui';
import { customerOrderApi } from '../../api/customerOrderApi';
import { formatCurrency } from '../../utils/menuUtils';

export interface OrderHistorySheetProps {
  isOpen: boolean;
  onClose: () => void;
  tableInfo?: { tableNumber: string; tableName: string };
  onSessionExpired?: () => void;
  refreshKey?: number;
}

export const OrderHistorySheet: React.FC<OrderHistorySheetProps> = ({
  isOpen,
  onClose,
  tableInfo,
  onSessionExpired,
  refreshKey,
}) => {
  const [orders, setOrders] = useState<CustomerOrderResponseDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const fetchOrders = useCallback(async () => {
    setIsLoading(true);
    setErrorMessage(null);
    try {
      const data = await customerOrderApi.getOrders();
      // Sort by roundNumber descending (latest first)
      setOrders([...data].sort((a, b) => b.roundNumber - a.roundNumber));
    } catch (err: unknown) {
      setErrorMessage('Sipariş geçmişi yüklenemedi. Lütfen tekrar deneyiniz.');
      if (err instanceof Error && 'status' in err && (err as { status: number }).status === 401) {
        onSessionExpired?.();
      }
    } finally {
      setIsLoading(false);
    }
  }, [onSessionExpired]);

  useEffect(() => {
    if (isOpen) {
      fetchOrders();
    }
  }, [isOpen, fetchOrders, refreshKey]);

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'Submitted':
        return { label: 'Mutfak Onayı Bekliyor', bg: '#dbeafe', color: '#1e40af' };
      case 'InPreparation':
        return { label: 'Hazırlanıyor', bg: '#fef3c7', color: '#92400e' };
      case 'Ready':
        return { label: 'Hazır', bg: '#dcfce7', color: '#166534' };
      case 'Served':
        return { label: 'Servis Edildi', bg: '#f3f4f6', color: '#374151' };
      case 'Cancelled':
        return { label: 'İptal Edildi', bg: '#fee2e2', color: '#991b1b' };
      default:
        return { label: status, bg: '#f3f4f6', color: '#374151' };
    }
  };

  const formatTime = (isoString: string) => {
    try {
      const date = new Date(isoString);
      return date.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
    } catch {
      return '';
    }
  };

  return (
    <BottomSheet
      isOpen={isOpen}
      onClose={onClose}
      title={
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', width: '100%' }}>
          <span>Masa Siparişleri</span>
        </div>
      }
      description={
        tableInfo
          ? `Masa ${tableInfo.tableNumber} (${tableInfo.tableName}) • Sipariş Geçmişi`
          : 'Bu masada verilen tüm siparişler'
      }
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          maxHeight: '75vh',
          boxSizing: 'border-box',
          gap: 'var(--ro-space-3)',
        }}
        data-testid="order-history-sheet"
      >
        {/* Header toolbar with manual Refresh button */}
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            paddingBottom: 'var(--ro-space-2)',
            borderBottom: '1px solid var(--ro-color-border)',
          }}
        >
          <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
            {orders.length} sipariş kaydı bulundu
          </span>

          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={fetchOrders}
            disabled={isLoading}
            data-testid="refresh-orders-btn"
            style={{ minHeight: '36px' }}
          >
            {isLoading ? 'Yenileniyor...' : '🔄 Yenile'}
          </Button>
        </div>

        {/* Error message if any */}
        {errorMessage && (
          <div
            role="alert"
            style={{
              padding: 'var(--ro-space-2) var(--ro-space-3)',
              backgroundColor: 'var(--ro-color-error-subtle, #fef2f2)',
              color: 'var(--ro-color-error-text, #991b1b)',
              borderRadius: 'var(--ro-radius-md)',
              fontSize: 'var(--ro-font-size-xs)',
            }}
            data-testid="order-history-error-alert"
          >
            {errorMessage}
          </div>
        )}

        {/* Orders list scroll area */}
        <div
          style={{
            flex: 1,
            overflowY: 'auto',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--ro-space-3)',
          }}
          data-testid="order-history-items-container"
        >
          {orders.length === 0 && !isLoading ? (
            <div
              style={{
                textAlign: 'center',
                padding: 'var(--ro-space-8) var(--ro-space-4)',
                color: 'var(--ro-color-text-muted)',
              }}
              data-testid="order-history-empty"
            >
              <span style={{ fontSize: '36px' }} role="img" aria-label="Boş">
                📋
              </span>
              <p style={{ marginTop: 'var(--ro-space-2)', fontSize: 'var(--ro-font-size-sm)' }}>
                Bu masada henüz verilmiş bir sipariş bulunmamaktadır.
              </p>
            </div>
          ) : (
            orders.map((order) => {
              const badge = getStatusBadge(order.status);
              const timeStr = formatTime(order.createdAtUtc);

              return (
                <div
                  key={order.id}
                  style={{
                    backgroundColor: 'var(--ro-color-surface-subtle, #f9fafb)',
                    borderRadius: 'var(--ro-radius-md)',
                    border: '1px solid var(--ro-color-border)',
                    padding: 'var(--ro-space-3)',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 'var(--ro-space-2)',
                  }}
                  data-testid={`order-history-card-round-${order.roundNumber}`}
                >
                  {/* Round & Status Header */}
                  <div
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
                      <span
                        style={{
                          fontWeight: 'var(--ro-font-weight-bold)',
                          fontSize: 'var(--ro-font-size-sm)',
                          color: 'var(--ro-color-text-primary)',
                        }}
                      >
                        Sipariş Turu #{order.roundNumber}
                      </span>
                      {timeStr && (
                        <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
                          • {timeStr}
                        </span>
                      )}
                    </div>

                    <span
                      style={{
                        padding: '2px 8px',
                        borderRadius: 'var(--ro-radius-full)',
                        backgroundColor: badge.bg,
                        color: badge.color,
                        fontSize: '11px',
                        fontWeight: 'var(--ro-font-weight-bold)',
                      }}
                      data-testid={`order-status-badge-${order.roundNumber}`}
                    >
                      {badge.label}
                    </span>
                  </div>

                  {/* Order items list */}
                  <div
                    style={{
                      borderTop: '1px solid var(--ro-color-border-subtle, #f3f4f6)',
                      paddingTop: 'var(--ro-space-2)',
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 'var(--ro-space-1)',
                    }}
                  >
                    {order.items.map((item) => (
                      <div
                        key={item.id}
                        style={{
                          display: 'flex',
                          justifyContent: 'space-between',
                          fontSize: 'var(--ro-font-size-xs)',
                          color: 'var(--ro-color-text-secondary)',
                        }}
                      >
                        <span>
                          {item.quantity}x {item.itemName}
                          {item.variantName ? ` (${item.variantName})` : ''}
                        </span>
                        <span style={{ fontWeight: 'var(--ro-font-weight-medium)' }}>
                          {formatCurrency(item.lineTotalMinorUnits, order.currency)}
                        </span>
                      </div>
                    ))}
                  </div>

                  {/* Order total */}
                  <div
                    style={{
                      borderTop: '1px dashed var(--ro-color-border)',
                      paddingTop: 'var(--ro-space-2)',
                      display: 'flex',
                      justifyContent: 'space-between',
                      fontSize: 'var(--ro-font-size-xs)',
                      fontWeight: 'var(--ro-font-weight-bold)',
                    }}
                  >
                    <span>Tur Toplamı</span>
                    <span>{formatCurrency(order.totalMinorUnits, order.currency)}</span>
                  </div>
                </div>
              );
            })
          )}
        </div>

        {/* Footer info notice */}
        <div
          style={{
            fontSize: '11px',
            color: 'var(--ro-color-text-muted)',
            textAlign: 'center',
            paddingTop: 'var(--ro-space-2)',
            borderTop: '1px solid var(--ro-color-border)',
          }}
          data-testid="order-history-signalr-notice"
        >
          Sipariş durumları mutfak hazırlığına göre güncellenir. Güncel durumu görmek için 'Yenile' butonunu kullanabilirsiniz.
        </div>
      </div>
    </BottomSheet>
  );
};
