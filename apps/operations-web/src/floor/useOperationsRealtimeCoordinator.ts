import { useState, useEffect, useRef, useCallback } from 'react';
import {
  StaffServiceRequestDto,
  ReadyOrderItemDto,
  TerminalContextDto,
  UserPrincipalDto,
} from '@restaurant-order/contracts';
import {
  useRealtimeEvents,
  useAudioChime,
  markEventReceivedInForeground,
  RealtimeConnectionStatus,
} from '@restaurant-order/ui';
import {
  fetchActiveServiceRequests,
  fetchBranchReadyItems,
  acknowledgeServiceRequest,
  completeServiceRequest,
  ApiError,
} from './api';

export interface OperationalNotification {
  id: string;
  title: string;
  subtitle?: string;
  time: string;
  eventType: string;
  tableId?: string;
  tableNumber?: string;
  isRead: boolean;
}

export interface OperationsRealtimeCoordinatorOptions {
  terminal: TerminalContextDto | null;
  staffUser: UserPrincipalDto | null;
  onRefreshFloor?: () => Promise<void> | void;
  onSessionRevoked?: () => void;
}

export function useOperationsRealtimeCoordinator({
  terminal,
  staffUser,
  onRefreshFloor,
  onSessionRevoked,
}: OperationsRealtimeCoordinatorOptions) {
  const [activeRequests, setActiveRequests] = useState<StaffServiceRequestDto[]>([]);
  const [readyItems, setReadyItems] = useState<ReadyOrderItemDto[]>([]);
  const [notifications, setNotifications] = useState<OperationalNotification[]>([]);
  const [isOperating, setIsOperating] = useState<boolean>(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [isStale, setIsStale] = useState<boolean>(false);
  const [syncError, setSyncError] = useState<string | null>(null);

  const { playChime } = useAudioChime();

  const processedEventIdsRef = useRef<Set<string>>(new Set());
  const serviceRequestsTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const readyItemsTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const floorTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);
  const epochRef = useRef<number>(0);

  const branchId = terminal?.branchId;
  const tenantId = terminal?.tenantId;
  const userId = staffUser?.userId;
  const isEnabled = Boolean(staffUser && branchId);

  // Otoriter aktif servis çağrılarını sunucudan çekme
  const loadActiveRequests = useCallback(async (throwOnError = false) => {
    if (!branchId) return;
    const currentEpoch = epochRef.current;
    const signal = abortControllerRef.current?.signal;

    try {
      const list = await fetchActiveServiceRequests(branchId, signal);
      if (currentEpoch !== epochRef.current) return;
      const activeOnly = list.filter((r) => r.status === 'Open' || r.status === 'Acknowledged');
      setActiveRequests(activeOnly);
      setIsStale(false);
      setSyncError(null);
    } catch (err: unknown) {
      if (signal?.aborted || currentEpoch !== epochRef.current) return;
      setIsStale(true);
      setSyncError(err instanceof Error ? err.message : 'Servis çağrıları alınamadı.');
      if (throwOnError) throw err;
    }
  }, [branchId]);

  // Otoriter hazır ürünleri sunucudan çekme
  const loadReadyItems = useCallback(async (throwOnError = false) => {
    if (!branchId) return;
    const currentEpoch = epochRef.current;
    const signal = abortControllerRef.current?.signal;

    try {
      const items = await fetchBranchReadyItems(branchId, signal);
      if (currentEpoch !== epochRef.current) return;
      setReadyItems(items);
      setIsStale(false);
      setSyncError(null);
    } catch (err: unknown) {
      if (signal?.aborted || currentEpoch !== epochRef.current) return;
      setIsStale(true);
      setSyncError(err instanceof Error ? err.message : 'Hazır siparişler alınamadı.');
      if (throwOnError) throw err;
    }
  }, [branchId]);

  // Resync ve başlangıç veri yüklemesi: kritik snapshot başarısızsa hatayı üst hook'a taşır
  const fullResync = useCallback(async () => {
    setNotifications([]);
    setIsStale(false);
    setSyncError(null);
    try {
      await Promise.all([
        loadActiveRequests(true),
        loadReadyItems(true),
        onRefreshFloor ? onRefreshFloor() : Promise.resolve(),
      ]);
    } catch (err: unknown) {
      setIsStale(true);
      setSyncError(err instanceof Error ? err.message : 'Yenileme başarısız oldu.');
      throw err;
    }
  }, [loadActiveRequests, loadReadyItems, onRefreshFloor]);

  // Mount ve branch/personel/tenant değişiminde scope temizliği ve başlatma
  useEffect(() => {
    epochRef.current += 1;
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }
    abortControllerRef.current = new AbortController();

    if (serviceRequestsTimerRef.current) clearTimeout(serviceRequestsTimerRef.current);
    if (readyItemsTimerRef.current) clearTimeout(readyItemsTimerRef.current);
    if (floorTimerRef.current) clearTimeout(floorTimerRef.current);

    processedEventIdsRef.current.clear();

    if (!isEnabled) {
      setActiveRequests([]);
      setReadyItems([]);
      setNotifications([]);
      setIsStale(false);
      setSyncError(null);
      return;
    }

    void fullResync().catch(() => {});

    return () => {
      if (serviceRequestsTimerRef.current) clearTimeout(serviceRequestsTimerRef.current);
      if (readyItemsTimerRef.current) clearTimeout(readyItemsTimerRef.current);
      if (floorTimerRef.current) clearTimeout(floorTimerRef.current);
      abortControllerRef.current?.abort();
    };
  }, [isEnabled, branchId, tenantId, userId, fullResync]);

  // Coalesced / Debounced yenileme yardımcıları (epoch korumalı)
  const scheduleRequestsRefresh = useCallback(() => {
    if (serviceRequestsTimerRef.current) clearTimeout(serviceRequestsTimerRef.current);
    const scheduledEpoch = epochRef.current;
    serviceRequestsTimerRef.current = setTimeout(() => {
      if (scheduledEpoch === epochRef.current) {
        void loadActiveRequests();
      }
    }, 60);
  }, [loadActiveRequests]);

  const scheduleReadyItemsRefresh = useCallback(() => {
    if (readyItemsTimerRef.current) clearTimeout(readyItemsTimerRef.current);
    const scheduledEpoch = epochRef.current;
    readyItemsTimerRef.current = setTimeout(() => {
      if (scheduledEpoch === epochRef.current) {
        void loadReadyItems();
      }
    }, 60);
  }, [loadReadyItems]);

  const scheduleFloorRefresh = useCallback(() => {
    if (!onRefreshFloor) return;
    if (floorTimerRef.current) clearTimeout(floorTimerRef.current);
    const scheduledEpoch = epochRef.current;
    floorTimerRef.current = setTimeout(() => {
      if (scheduledEpoch === epochRef.current) {
        void onRefreshFloor();
      }
    }, 80);
  }, [onRefreshFloor]);

  // Realtime hook entegrasyonu
  const { status: realtimeStatus } = useRealtimeEvents({
    enabled: isEnabled,
    tenantId: terminal?.tenantId,
    branchId: terminal?.branchId,
    onResyncRequired: async () => {
      await fullResync();
    },
    onSessionClosed: () => {
      // Sunucu revoke/kapanma olayında yerel korunan verileri hemen temizle ve terminali kilitle
      setActiveRequests([]);
      setReadyItems([]);
      setNotifications([]);
      processedEventIdsRef.current.clear();
      onSessionRevoked?.();
    },
    onEvent: (event, meta) => {
      if (!event || !event.event_id) return;

      // Event deduplication: aynı event_id tekrar gelirse işlem yapma
      if (processedEventIdsRef.current.has(event.event_id)) {
        return;
      }
      processedEventIdsRef.current.add(event.event_id);
      if (processedEventIdsRef.current.size > 500) {
        const first = Array.from(processedEventIdsRef.current).slice(0, 100);
        for (const id of first) processedEventIdsRef.current.delete(id);
      }

      markEventReceivedInForeground(event.event_id);

      // Catch-up sırasında geçmiş ses çalınmaz
      if (!meta?.isCatchUp) {
        playChime();
      }

      const eventData = (event.data || {}) as Record<string, unknown>;
      const eventType = event.event_type;

      let title = 'Yeni Operasyon Olayı';
      let subtitle: string | undefined;
      const tableNumber = (eventData.table_number || eventData.tableNumber) as string | undefined;
      const tableId = (eventData.table_id || eventData.tableId) as string | undefined;

      if (eventType === 'order.created') {
        title = 'Yeni Sipariş Alındı';
        scheduleReadyItemsRefresh();
        scheduleFloorRefresh();
      } else if (eventType === 'table.service_requested') {
        title = 'Masa Servis / Garson Çağrısı';
        subtitle = (eventData.note || eventData.request_type || eventData.requestType) as string | undefined;
        scheduleRequestsRefresh();
        scheduleFloorRefresh();
      } else if (eventType.startsWith('table.service_')) {
        title = 'Servis Çağrısı Güncellendi';
        subtitle = (eventData.note || eventData.request_type || eventData.requestType) as string | undefined;
        scheduleRequestsRefresh();
        scheduleFloorRefresh();
      } else if (eventType === 'order.item_status_changed') {
        title = 'Ürün Durumu Güncellendi';
        const itemStatus = (eventData.status || eventData.item_status) as string | undefined;
        if (itemStatus === 'Ready') {
          subtitle = (eventData.item_name || eventData.itemName) as string | undefined;
        }
        scheduleReadyItemsRefresh();
        scheduleFloorRefresh();
      } else if (eventType === 'menu.item_availability_changed') {
        title = 'Menü Stok Durumu Değişti';
      } else if (eventType.startsWith('table.session_')) {
        title = 'Masa Oturumu Güncellendi';
        scheduleFloorRefresh();
      }

      // Olay akışına ekle
      const newNotification: OperationalNotification = {
        id: event.event_id,
        title,
        subtitle,
        time: new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
        eventType,
        tableId,
        tableNumber,
        isRead: false,
      };

      setNotifications((prev) => [newNotification, ...prev.slice(0, 29)]);
    },
  });

  // Servis çağrısı kabul etme aksiyonu
  const handleAcknowledge = useCallback(
    async (requestId: string, concurrencyToken: string) => {
      if (!branchId) return;
      setIsOperating(true);
      setActionError(null);
      try {
        const updated = await acknowledgeServiceRequest(branchId, requestId, concurrencyToken);
        setActiveRequests((prev) =>
          prev.map((r) => (r.id === requestId ? updated : r))
        );
        scheduleFloorRefresh();
      } catch (err: unknown) {
        if (err instanceof ApiError && err.status === 409) {
          setActionError('Bu çağrı başka bir personel tarafından kabul edilmiş veya iptal edilmiş.');
        } else {
          setActionError(err instanceof Error ? err.message : 'Çağrı kabul edilemedi.');
        }
        void loadActiveRequests();
      } finally {
        setIsOperating(false);
      }
    },
    [branchId, loadActiveRequests, scheduleFloorRefresh]
  );

  // Servis çağrısı tamamlama aksiyonu
  const handleComplete = useCallback(
    async (requestId: string, concurrencyToken: string) => {
      if (!branchId) return;
      setIsOperating(true);
      setActionError(null);
      try {
        await completeServiceRequest(branchId, requestId, concurrencyToken);
        setActiveRequests((prev) => prev.filter((r) => r.id !== requestId));
        scheduleFloorRefresh();
      } catch (err: unknown) {
        if (err instanceof ApiError && err.status === 409) {
          setActionError('Bu çağrı başka bir işlem tarafından güncellenmiş.');
        } else {
          setActionError(err instanceof Error ? err.message : 'Çağrı tamamlanamadı.');
        }
        void loadActiveRequests();
      } finally {
        setIsOperating(false);
      }
    },
    [branchId, loadActiveRequests, scheduleFloorRefresh]
  );

  // Bildirim kapatma / tümünü temizleme (çağrıyı değil sadece UI bildirimini etkiler)
  const handleDismissNotification = useCallback((id: string) => {
    setNotifications((prev) => prev.filter((n) => n.id !== id));
  }, []);

  const handleClearAllNotifications = useCallback(() => {
    setNotifications([]);
  }, []);

  return {
    realtimeStatus: realtimeStatus as RealtimeConnectionStatus,
    activeRequests,
    readyItems,
    notifications,
    isOperating,
    actionError,
    setActionError,
    isStale,
    syncError,
    loadActiveRequests,
    loadReadyItems,
    fullResync,
    handleAcknowledge,
    handleComplete,
    handleDismissNotification,
    handleClearAllNotifications,
  };
}
