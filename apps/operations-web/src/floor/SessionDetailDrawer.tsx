import React, { useState, useEffect, useCallback } from 'react';
import {
  Drawer,
  Button,
  Badge,
  Spinner,
  Input,
  FormField,
} from '@restaurant-order/ui';
import {
  TableFloorStatusDto,
  DiningSessionDto,
  StaffOrderResponseDto,
  StaffOrderItemDto,
  StaffServiceRequestDto,
} from '@restaurant-order/contracts';
import { useTerminalOptional, TerminalContextValue } from '../auth/TerminalContext';
import {
  resolveTableOperationalStatus,
  TABLE_STATUS_VISUALS,
  calculateSeatedDurationMinutes,
} from './types';
import { SessionOrderRoundsList } from './SessionOrderRoundsList';
import { StaffOrderDrawer } from './StaffOrderDrawer';
import { CancelItemModal } from './CancelItemModal';
import {
  fetchSessionDetail,
  fetchSessionOrders,
  fetchTableServiceRequests,
  openSession,
  activateSession,
  requestBill,
  acknowledgeServiceRequest,
  completeServiceRequest,
  ApiError,
} from './api';

export interface SessionDetailDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  branchId: string;
  tableFloorDto: TableFloorStatusDto | null;
  onRefreshFloor: () => Promise<void>;
  onOpenMovementModal: (sourceTable: TableFloorStatusDto) => void;
  terminalContext?: TerminalContextValue;
}

export const SessionDetailDrawer: React.FC<SessionDetailDrawerProps> = ({
  isOpen,
  onClose,
  branchId,
  tableFloorDto,
  onRefreshFloor,
  onOpenMovementModal,
  terminalContext: customTerminalContext,
}) => {
  const optionalContext = useTerminalOptional();
  const terminalContext: TerminalContextValue = customTerminalContext || optionalContext || {
    terminal: null,
    state: 'AUTHENTICATED',
    isEnrolled: true,
    staffUser: null,
    isLoading: false,
    error: null,
    activateTerminal: async () => false,
    loginWithPin: async () => false,
    logoutStaff: async () => {},
    deactivateTerminal: async () => {},
  };
  const [session, setSession] = useState<DiningSessionDto | null>(null);
  const [orders, setOrders] = useState<StaffOrderResponseDto[]>([]);
  const [serviceRequests, setServiceRequests] = useState<StaffServiceRequestDto[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [guestCountInput, setGuestCountInput] = useState<number>(2);

  const [isOrderDrawerOpen, setIsOrderDrawerOpen] = useState<boolean>(false);
  const [cancellingItemData, setCancellingItemData] = useState<{
    order: StaffOrderResponseDto;
    item: StaffOrderItemDto;
  } | null>(null);

  const table = tableFloorDto?.table;
  const initialSession = tableFloorDto?.activeSession;

  // Oturum ve ilişkili verileri yükleme
  const loadDetailData = useCallback(async (sessionId: string) => {
    setIsLoading(true);
    setActionError(null);
    try {
      const [sessionData, ordersData, requestsData] = await Promise.all([
        fetchSessionDetail(branchId, sessionId),
        fetchSessionOrders(branchId, sessionId),
        fetchTableServiceRequests(branchId, table?.id, sessionId),
      ]);
      setSession(sessionData);
      setOrders(ordersData);
      setServiceRequests(requestsData);
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        setActionError(err.message);
      } else {
        setActionError('Oturum bilgileri yüklenemedi.');
      }
    } finally {
      setIsLoading(false);
    }
  }, [branchId, table?.id]);

  useEffect(() => {
    if (!isOpen || !tableFloorDto) {
      setSession(null);
      setOrders([]);
      setServiceRequests([]);
      setActionError(null);
      return;
    }

    if (initialSession?.id) {
      loadDetailData(initialSession.id);
    } else {
      setSession(null);
      setOrders([]);
      // Masa oturumu yoksa bile masaya ait servis çağrılarını kontrol et
      if (table?.id) {
        fetchTableServiceRequests(branchId, table.id)
          .then(setServiceRequests)
          .catch(() => {});
      }
    }
  }, [isOpen, tableFloorDto, initialSession?.id, branchId, table?.id, loadDetailData]);

  if (!tableFloorDto || !table) return null;

  const currentStatus = resolveTableOperationalStatus(tableFloorDto);
  const visual = TABLE_STATUS_VISUALS[currentStatus];
  const activeSessionId = session?.id || initialSession?.id;
  const seatedDuration = calculateSeatedDurationMinutes(session?.createdAtUtc || initialSession?.createdAtUtc);

  // Aksiyonlar
  const handleOpenSession = async () => {
    setIsSubmitting(true);
    setActionError(null);
    try {
      await openSession(branchId, table.id, guestCountInput);
      await onRefreshFloor();
      onClose();
    } catch (err: unknown) {
      setActionError(err instanceof ApiError ? err.message : 'Oturum açılamadı.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleActivateSession = async () => {
    if (!activeSessionId) return;
    const token = session?.concurrencyToken || initialSession?.concurrencyToken;
    if (!token) return;

    setIsSubmitting(true);
    setActionError(null);
    try {
      await activateSession(branchId, activeSessionId, token);
      await onRefreshFloor();
      await loadDetailData(activeSessionId);
    } catch (err: unknown) {
      setActionError(err instanceof ApiError ? err.message : 'Oturum aktifleştirilemedi.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleRequestBill = async () => {
    if (!activeSessionId) return;
    const token = session?.concurrencyToken || initialSession?.concurrencyToken;
    if (!token) return;

    setIsSubmitting(true);
    setActionError(null);
    try {
      await requestBill(branchId, activeSessionId, token);
      await onRefreshFloor();
      await loadDetailData(activeSessionId);
    } catch (err: unknown) {
      setActionError(err instanceof ApiError ? err.message : 'Hesap isteme işlemi başarısız oldu.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleAcknowledgeRequest = async (sr: StaffServiceRequestDto) => {
    setIsSubmitting(true);
    setActionError(null);
    try {
      await acknowledgeServiceRequest(branchId, sr.id, sr.concurrencyToken);
      if (activeSessionId) await loadDetailData(activeSessionId);
      await onRefreshFloor();
    } catch (err: unknown) {
      setActionError(err instanceof ApiError ? err.message : 'Çağrı kabul edilemedi.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleCompleteRequest = async (sr: StaffServiceRequestDto) => {
    setIsSubmitting(true);
    setActionError(null);
    try {
      await completeServiceRequest(branchId, sr.id, sr.concurrencyToken);
      if (activeSessionId) await loadDetailData(activeSessionId);
      await onRefreshFloor();
    } catch (err: unknown) {
      setActionError(err instanceof ApiError ? err.message : 'Çağrı tamamlanamadı.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Drawer
      isOpen={isOpen}
      onClose={onClose}
      title={`${table.tableNumber} - ${table.name}`}
      size="md"
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', paddingBottom: '1.5rem' }}>
        {/* Üst Bilgi Kartı */}
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            padding: '12px',
            backgroundColor: 'var(--ro-color-surface-hover, #f3f4f6)',
            borderRadius: '8px',
          }}
        >
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <Badge variant={visual.badgeVariant}>
                <span aria-hidden="true">{visual.icon}</span> {visual.label}
              </Badge>
              <span style={{ fontSize: '0.85rem', color: '#4b5563' }}>👥 Kapasite: {table.capacity}</span>
            </div>
            {activeSessionId && (
              <div style={{ fontSize: '0.8rem', color: '#6b7280', marginTop: '4px' }}>
                ⏱️ Oturma Süresi: <strong>{seatedDuration} dk</strong>
              </div>
            )}
          </div>

          {activeSessionId && session?.status === 'Active' && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => onOpenMovementModal(tableFloorDto)}
              style={{ minHeight: '44px' }}
              title="Masayı başka masaya taşı veya birleştir"
            >
              🔄 Taşı / Birleştir
            </Button>
          )}
        </div>

        {/* Hata Uyarısı */}
        {actionError && (
          <div
            role="alert"
            style={{
              padding: '10px 12px',
              backgroundColor: '#fee2e2',
              color: '#b91c1c',
              borderRadius: '6px',
              fontSize: '0.9rem',
              border: '1px solid #f87171',
            }}
          >
            ⚠️ {actionError}
          </div>
        )}

        {/* Oturum Yoksa: Oturum Açma Formu */}
        {!activeSessionId ? (
          <div style={{ padding: '16px', border: '1px dashed #d1d5db', borderRadius: '8px', textAlign: 'center' }}>
            <p style={{ fontWeight: 600, margin: '0 0 12px' }}>Bu masada aktif bir oturum bulunmuyor.</p>
            <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '8px', marginBottom: '16px' }}>
              <FormField id="guest-count-input" label="Kişi Sayısı:">
                <Input
                  type="number"
                  min={1}
                  max={table.capacity * 2}
                  value={guestCountInput}
                  onChange={(e) => setGuestCountInput(Math.max(1, parseInt(e.target.value, 10) || 1))}
                  style={{ width: '80px', textAlign: 'center', minHeight: '44px' }}
                />
              </FormField>
            </div>
            <Button
              variant="primary"
              size="md"
              onClick={handleOpenSession}
              disabled={isSubmitting || !table.isActive}
              style={{ minHeight: '44px', width: '100%', maxWidth: '240px' }}
            >
              {isSubmitting ? <Spinner size="sm" /> : 'Oturumu Aç'}
            </Button>
          </div>
        ) : isLoading ? (
          <div style={{ textAlign: 'center', padding: '2rem' }}>
            <Spinner size="md" />
            <p style={{ marginTop: '0.5rem', color: '#6b7280' }}>Oturum ve siparişler yükleniyor...</p>
          </div>
        ) : (
          <>
            {/* Açık Servis Çağrıları Bölümü */}
            {serviceRequests.filter((r) => r.status !== 'Completed' && r.status !== 'Cancelled').length > 0 && (
              <div style={{ border: '1px solid #fed7aa', backgroundColor: '#fff7ed', borderRadius: '8px', padding: '12px' }}>
                <h3 style={{ margin: '0 0 8px', fontSize: '0.95rem', color: '#c2410c', display: 'flex', alignItems: 'center', gap: '6px' }}>
                  🔔 Bekleyen Servis Çağrıları
                </h3>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                  {serviceRequests.filter((r) => r.status !== 'Completed' && r.status !== 'Cancelled').map((sr) => (
                    <div key={sr.id} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', backgroundColor: '#ffffff', padding: '8px 10px', borderRadius: '6px', border: '1px solid #fdba74' }}>
                      <div>
                        <strong>{sr.requestType}</strong>
                        {sr.note && <span style={{ fontSize: '0.85rem', color: '#6b7280', display: 'block' }}>"{sr.note}"</span>}
                        <span style={{ fontSize: '0.75rem', color: '#9a3412' }}>Durum: {sr.status}</span>
                      </div>
                      <div style={{ display: 'flex', gap: '4px' }}>
                        {sr.status === 'Open' && (
                          <Button variant="outline" size="sm" onClick={() => handleAcknowledgeRequest(sr)} disabled={isSubmitting} style={{ minHeight: '44px' }}>Kabul Et</Button>
                        )}
                        <Button variant="primary" size="sm" onClick={() => handleCompleteRequest(sr)} disabled={isSubmitting} style={{ minHeight: '44px' }}>Tamamla</Button>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Sipariş Turları & Sipariş Özeti (WAIT-06 Sınırı: Yalnızca Sipariş Toplamı Önizlemesi) */}
            <SessionOrderRoundsList
              orders={orders}
              onCancelItem={(ord, it) => setCancellingItemData({ order: ord, item: it })}
            />

            {/* Durum Yönetim Butonları */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '8px' }}>
              {session?.status === 'Active' && (
                <Button
                  variant="primary"
                  size="md"
                  onClick={() => setIsOrderDrawerOpen(true)}
                  disabled={isSubmitting}
                  style={{ minHeight: '44px', width: '100%', fontWeight: 600 }}
                >
                  ➕ Yeni Sipariş Gir (Hızlı Menü)
                </Button>
              )}

              {session?.status === 'Open' && (
                <Button
                  variant="primary"
                  size="md"
                  onClick={handleActivateSession}
                  disabled={isSubmitting}
                  style={{ minHeight: '44px', width: '100%' }}
                >
                  {isSubmitting ? <Spinner size="sm" /> : 'Oturumu Başlat / Aktifleştir'}
                </Button>
              )}

              {session?.status === 'Active' && (
                <Button
                  variant="danger"
                  size="md"
                  onClick={handleRequestBill}
                  disabled={isSubmitting}
                  style={{ minHeight: '44px', width: '100%' }}
                >
                  {isSubmitting ? <Spinner size="sm" /> : 'Hesap İste (Bill Requested)'}
                </Button>
              )}
            </div>
          </>
        )}
      </div>

      {isOrderDrawerOpen && session && table && (
        <StaffOrderDrawer
          isOpen={isOrderDrawerOpen}
          branchId={branchId}
          session={session}
          tableNumber={table.tableNumber}
          terminalContext={terminalContext}
          onClose={() => setIsOrderDrawerOpen(false)}
          onOrderCreated={async () => {
            await loadDetailData(session.id);
            await onRefreshFloor();
          }}
        />
      )}

      {cancellingItemData && session && (
        <CancelItemModal
          isOpen={Boolean(cancellingItemData)}
          branchId={branchId}
          sessionId={session.id}
          order={cancellingItemData.order}
          item={cancellingItemData.item}
          terminalContext={terminalContext}
          onClose={() => setCancellingItemData(null)}
          onItemCancelled={async () => {
            await loadDetailData(session.id);
            await onRefreshFloor();
          }}
        />
      )}
    </Drawer>
  );
};
