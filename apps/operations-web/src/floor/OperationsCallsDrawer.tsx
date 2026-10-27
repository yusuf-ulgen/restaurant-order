import React, { useState } from 'react';
import {
  Modal,
  Button,
  Badge,
  EmptyState,
  Spinner,
} from '@restaurant-order/ui';
import {
  StaffServiceRequestDto,
  ReadyOrderItemDto,
} from '@restaurant-order/contracts';
import { OperationalNotification } from './useOperationsRealtimeCoordinator';

export interface OperationsCallsDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  activeRequests: StaffServiceRequestDto[];
  readyItems: ReadyOrderItemDto[];
  notifications: OperationalNotification[];
  onAcknowledgeRequest: (requestId: string, concurrencyToken: string) => Promise<void>;
  onCompleteRequest: (requestId: string, concurrencyToken: string) => Promise<void>;
  onDismissNotification: (id: string) => void;
  onClearAllNotifications: () => void;
  onGoToTable?: (tableId: string) => void;
  isOperating: boolean;
  actionError?: string | null;
  onClearActionError?: () => void;
  realtimeStatus?: string;
}

type TabType = 'calls' | 'ready' | 'stream';

function formatWaitingTime(createdAtUtc: string): string {
  try {
    const diffMs = Date.now() - new Date(createdAtUtc).getTime();
    const diffSec = Math.floor(diffMs / 1000);
    if (diffSec < 60) return `${Math.max(1, diffSec)} sn önce`;
    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) return `${diffMin} dk önce`;
    const diffHour = Math.floor(diffMin / 60);
    return `${diffHour} sa önce`;
  } catch {
    return 'Az önce';
  }
}

function getRequestTypeLabel(type: string): string {
  switch (type) {
    case 'Waiter': return 'Garson Çağrısı';
    case 'Water': return 'Su İsteği';
    case 'Napkin': return 'Peçete İsteği';
    case 'Assistance': return 'Yardım / Bilgi';
    default: return type;
  }
}

export const OperationsCallsDrawer: React.FC<OperationsCallsDrawerProps> = ({
  isOpen,
  onClose,
  activeRequests,
  readyItems,
  notifications,
  onAcknowledgeRequest,
  onCompleteRequest,
  onDismissNotification,
  onClearAllNotifications,
  onGoToTable,
  isOperating,
  actionError,
  onClearActionError,
  realtimeStatus,
}) => {
  const [activeTab, setActiveTab] = useState<TabType>('calls');
  const [operatingId, setOperatingId] = useState<string | null>(null);

  const handleAction = async (id: string, action: () => Promise<void>) => {
    setOperatingId(id);
    try {
      await action();
    } finally {
      setOperatingId(null);
    }
  };

  const handleTableNavigation = (tableId: string) => {
    onClose();
    if (onGoToTable) {
      onGoToTable(tableId);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Aktif Çağrılar"
      description="Masalardan gelen servis ve hesap çağrıları"
      size="md"
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', minHeight: '380px' }}>
        {/* Bağlantı uyarısı */}
        {realtimeStatus && realtimeStatus !== 'connected' && (
          <div
            role="alert"
            style={{
              padding: '8px 12px',
              backgroundColor: '#fffbeb',
              border: '1px solid #fef08a',
              borderRadius: '6px',
              fontSize: '0.85rem',
              color: '#92400e',
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
            }}
          >
            <span>⚠️ Canlı bağlantı durumu: {realtimeStatus}. Yeni çağrılar için liste otomatik yenilenecektir.</span>
          </div>
        )}

        {/* Hata uyarısı */}
        {actionError && (
          <div
            role="alert"
            style={{
              padding: '8px 12px',
              backgroundColor: '#fef2f2',
              border: '1px solid #fecaca',
              borderRadius: '6px',
              fontSize: '0.85rem',
              color: '#991b1b',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
            }}
          >
            <span>{actionError}</span>
            {onClearActionError && (
              <Button variant="ghost" size="sm" onClick={onClearActionError} style={{ minHeight: '28px' }}>
                ✕
              </Button>
            )}
          </div>
        )}

        {/* Sekme butonları */}
        <div style={{ display: 'flex', borderBottom: '1px solid var(--ro-color-border, #e5e7eb)', gap: '0.5rem' }}>
          <button
            type="button"
            onClick={() => setActiveTab('calls')}
            style={{
              padding: '8px 14px',
              border: 'none',
              background: 'none',
              cursor: 'pointer',
              fontWeight: activeTab === 'calls' ? 600 : 400,
              borderBottom: activeTab === 'calls' ? '2px solid var(--ro-color-primary, #2563eb)' : '2px solid transparent',
              color: activeTab === 'calls' ? 'var(--ro-color-primary, #2563eb)' : '#6b7280',
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
            }}
          >
            <span>🔔 Aktif Çağrılar</span>
            <Badge variant={activeRequests.length > 0 ? 'warning' : 'neutral'}>
              {activeRequests.length}
            </Badge>
          </button>

          <button
            type="button"
            onClick={() => setActiveTab('ready')}
            style={{
              padding: '8px 14px',
              border: 'none',
              background: 'none',
              cursor: 'pointer',
              fontWeight: activeTab === 'ready' ? 600 : 400,
              borderBottom: activeTab === 'ready' ? '2px solid var(--ro-color-primary, #2563eb)' : '2px solid transparent',
              color: activeTab === 'ready' ? 'var(--ro-color-primary, #2563eb)' : '#6b7280',
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
            }}
          >
            <span>🍽️ Hazır Ürünler</span>
            <Badge variant={readyItems.length > 0 ? 'success' : 'neutral'}>
              {readyItems.length}
            </Badge>
          </button>

          <button
            type="button"
            onClick={() => setActiveTab('stream')}
            style={{
              padding: '8px 14px',
              border: 'none',
              background: 'none',
              cursor: 'pointer',
              fontWeight: activeTab === 'stream' ? 600 : 400,
              borderBottom: activeTab === 'stream' ? '2px solid var(--ro-color-primary, #2563eb)' : '2px solid transparent',
              color: activeTab === 'stream' ? 'var(--ro-color-primary, #2563eb)' : '#6b7280',
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
            }}
          >
            <span>📜 Olay Akışı</span>
            <Badge variant="neutral">{notifications.length}</Badge>
          </button>
        </div>

        {/* Tab 1: Aktif Çağrılar */}
        {activeTab === 'calls' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', maxHeight: '360px', overflowY: 'auto' }}>
            {activeRequests.length === 0 && notifications.length === 0 ? (
              <EmptyState
                title="Bekleyen Çağrı Yok"
                description="Şu anda masalardan iletilen açık bir garson veya hesap çağrısı bulunmamaktadır."
              />
            ) : activeRequests.length === 0 ? (
              notifications.map((notif) => (
                <div
                  key={notif.id}
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    padding: '10px 12px',
                    borderRadius: '6px',
                    backgroundColor: 'var(--ro-color-surface-hover, #f3f4f6)',
                    border: '1px solid var(--ro-color-border, #e5e7eb)',
                  }}
                >
                  <div>
                    <div style={{ fontWeight: 600, fontSize: '14px' }}>{notif.title}</div>
                    <div style={{ fontSize: '12px', color: '#6b7280' }}>{notif.time}</div>
                  </div>
                  <Badge variant="primary">{notif.eventType}</Badge>
                </div>
              ))
            ) : (
              activeRequests.map((req) => {
                const isItemBusy = operatingId === req.id || isOperating;
                const isAcknowledged = req.status === 'Acknowledged';

                return (
                  <div
                    key={req.id}
                    data-testid={`service-request-card-${req.id}`}
                    style={{
                      padding: '12px 14px',
                      borderRadius: '8px',
                      backgroundColor: 'var(--ro-color-surface-hover, #f9fafb)',
                      border: '1px solid var(--ro-color-border, #e5e7eb)',
                      display: 'flex',
                      flexDirection: 'column',
                      gap: '8px',
                    }}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                      <div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                          <span style={{ fontWeight: 700, fontSize: '0.95rem' }}>Masa {req.tableNumber}</span>
                          <Badge variant="neutral">{getRequestTypeLabel(req.requestType)}</Badge>
                          <Badge variant={isAcknowledged ? 'primary' : 'warning'}>
                            {isAcknowledged ? 'İşleme Alındı' : 'Bekliyor'}
                          </Badge>
                        </div>
                        {req.note && (
                          <p style={{ margin: '4px 0 0', fontSize: '0.85rem', color: '#4b5563' }}>
                            Not: "{req.note}"
                          </p>
                        )}
                      </div>
                      <span style={{ fontSize: '0.8rem', color: '#6b7280', whiteSpace: 'nowrap' }}>
                        {formatWaitingTime(req.createdAtUtc)}
                      </span>
                    </div>

                    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '4px' }}>
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => handleTableNavigation(req.tableId)}
                        disabled={isItemBusy}
                      >
                        Masaya Git
                      </Button>

                      {!isAcknowledged ? (
                        <Button
                          variant="primary"
                          size="sm"
                          onClick={() => handleAction(req.id, () => onAcknowledgeRequest(req.id, req.concurrencyToken))}
                          disabled={isItemBusy}
                        >
                          {isItemBusy ? <Spinner size="sm" /> : 'Kabul Et'}
                        </Button>
                      ) : (
                        <Button
                          variant="primary"
                          size="sm"
                          onClick={() => handleAction(req.id, () => onCompleteRequest(req.id, req.concurrencyToken))}
                          disabled={isItemBusy}
                        >
                          {isItemBusy ? <Spinner size="sm" /> : 'Tamamla'}
                        </Button>
                      )}
                    </div>
                  </div>
                );
              })
            )}
          </div>
        )}

        {/* Tab 2: Hazır Ürünler */}
        {activeTab === 'ready' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', maxHeight: '360px', overflowY: 'auto' }}>
            {readyItems.length === 0 ? (
              <EmptyState
                title="Servis Bekleyen Hazır Ürün Yok"
                description="Mutfak veya bar KDS tarafından hazır olarak işaretlenmiş ürün bulunmamaktadır."
              />
            ) : (
              readyItems.map((item) => (
                <div
                  key={`${item.orderId}-${item.itemId}`}
                  data-testid={`ready-item-card-${item.itemId}`}
                  style={{
                    padding: '12px 14px',
                    borderRadius: '8px',
                    backgroundColor: '#f0fdf4',
                    border: '1px solid #bbf7d0',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                  }}
                >
                  <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                      <span style={{ fontWeight: 700, fontSize: '0.95rem' }}>Masa {item.tableNumber}</span>
                      <Badge variant="success">Hazır</Badge>
                      <span style={{ fontSize: '0.8rem', color: '#166534' }}>Tur #{item.roundNumber}</span>
                    </div>
                    <div style={{ fontSize: '0.9rem', fontWeight: 600, color: '#1e293b', marginTop: '2px' }}>
                      {item.quantity}x {item.itemName} {item.variantName ? `(${item.variantName})` : ''}
                    </div>
                  </div>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => handleTableNavigation(item.tableId)}
                  >
                    Masaya Git
                  </Button>
                </div>
              ))
            )}
          </div>
        )}

        {/* Tab 3: Olay Akışı */}
        {activeTab === 'stream' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span style={{ fontSize: '0.85rem', color: '#6b7280' }}>
                Canlı olay akışı (kapatılan bildirim çağrıyı sonlandırmaz)
              </span>
              {notifications.length > 0 && (
                <Button variant="ghost" size="sm" onClick={onClearAllNotifications}>
                  Tümünü Temizle
                </Button>
              )}
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', maxHeight: '320px', overflowY: 'auto' }}>
              {notifications.length === 0 ? (
                <EmptyState
                  title="Bildirim Bulunmuyor"
                  description="Son oturumda alınan yeni operasyonel bildirim bulunmamaktadır."
                />
              ) : (
                notifications.map((notif) => (
                  <div
                    key={notif.id}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      padding: '8px 12px',
                      borderRadius: '6px',
                      backgroundColor: 'var(--ro-color-surface-hover, #f3f4f6)',
                      border: '1px solid var(--ro-color-border, #e5e7eb)',
                    }}
                  >
                    <div>
                      <div style={{ fontWeight: 600, fontSize: '0.9rem' }}>{notif.title}</div>
                      {notif.subtitle && (
                        <div style={{ fontSize: '0.8rem', color: '#4b5563' }}>{notif.subtitle}</div>
                      )}
                      <div style={{ fontSize: '0.75rem', color: '#6b7280' }}>{notif.time}</div>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                      <Badge variant="neutral">{notif.eventType}</Badge>
                      <button
                        type="button"
                        onClick={() => onDismissNotification(notif.id)}
                        title="Bildirimi Kapat"
                        style={{
                          background: 'none',
                          border: 'none',
                          cursor: 'pointer',
                          color: '#9ca3af',
                          padding: '2px 4px',
                        }}
                      >
                        ✕
                      </button>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
};
