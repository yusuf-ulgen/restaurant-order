import React, { useState, useEffect, useCallback, useRef } from 'react';
import {
  Spinner,
  EmptyState,
  ErrorState,
  Button,
} from '@restaurant-order/ui';
import {
  TableFloorStatusDto,
  DiningAreaContract,
} from '@restaurant-order/contracts';
import { fetchFloorStatus, fetchDiningAreas, ApiError } from './api';
import { FloorPlanView } from './FloorPlanView';
import { SessionDetailDrawer } from './SessionDetailDrawer';
import { SessionMovementModal } from './SessionMovementModal';

export interface OperationsFloorContainerProps {
  branchId: string;
  terminalName?: string;
  onOpenCallsModal?: () => void;
  liveEventCount?: number;
  readyItemsCount?: number;
  focusedTableId?: string | null;
  onClearFocusedTable?: () => void;
  onRegisterRefreshFloor?: (refreshFn: () => Promise<void>) => void;
}

export const OperationsFloorContainer: React.FC<OperationsFloorContainerProps> = ({
  branchId,
  terminalName,
  onOpenCallsModal,
  liveEventCount = 0,
  readyItemsCount = 0,
  focusedTableId,
  onClearFocusedTable,
  onRegisterRefreshFloor,
}) => {
  const [tables, setTables] = useState<TableFloorStatusDto[]>([]);
  const [diningAreas, setDiningAreas] = useState<DiningAreaContract[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isStaleData, setIsStaleData] = useState<boolean>(false);

  // Seçili masa ve modallar
  const [selectedTable, setSelectedTable] = useState<TableFloorStatusDto | null>(null);
  const [isDetailDrawerOpen, setIsDetailDrawerOpen] = useState<boolean>(false);
  const [movementSourceTable, setMovementSourceTable] = useState<TableFloorStatusDto | null>(null);
  const [isMovementModalOpen, setIsMovementModalOpen] = useState<boolean>(false);

  const isMountedRef = useRef<boolean>(true);

  // Veri yükleme
  const loadFloorData = useCallback(async (isBackground = false) => {
    if (!branchId) return;

    if (!isBackground) {
      setIsLoading(true);
      setErrorMessage(null);
    }

    try {
      const [floorStatus, areas] = await Promise.all([
        fetchFloorStatus(branchId),
        fetchDiningAreas(branchId),
      ]);

      if (isMountedRef.current) {
        setTables(floorStatus.tables || []);
        setDiningAreas(areas || []);
        setErrorMessage(null);
        setIsStaleData(false);

        // Eğer açık bir detay çekmecesi varsa, seçili masayı yeni veriyle güncelle
        if (selectedTable) {
          const updated = floorStatus.tables?.find((t) => t.table.id === selectedTable.table.id);
          if (updated) setSelectedTable(updated);
        }
      }
    } catch (err: unknown) {
      if (isMountedRef.current) {
        if (tables.length > 0) {
          // Önceden yüklenmiş veri varsa silmeyip bayat veri (stale) uyarısı gösteriyoruz
          setIsStaleData(true);
        } else {
          setErrorMessage(err instanceof ApiError ? err.message : 'Kat planı verileri alınamadı.');
        }
      }
    } finally {
      if (isMountedRef.current && !isBackground) {
        setIsLoading(false);
      }
    }
  }, [branchId, selectedTable, tables.length]);

  useEffect(() => {
    isMountedRef.current = true;
    loadFloorData(false);

    if (onRegisterRefreshFloor) {
      onRegisterRefreshFloor(() => loadFloorData(true));
    }

    return () => {
      isMountedRef.current = false;
      // Kapsam değiştiğinde veya unmount olduğunda masaları temizle
      setTables([]);
      setDiningAreas([]);
      setSelectedTable(null);
    };
  }, [branchId, loadFloorData, onRegisterRefreshFloor]);

  // Dışarıdan bir masaya odaklanma talebi geldiğinde (Hazır kalem veya servis çağrısından)
  useEffect(() => {
    if (focusedTableId && tables.length > 0) {
      const target = tables.find((t) => t.table.id === focusedTableId);
      if (target) {
        setSelectedTable(target);
        setIsDetailDrawerOpen(true);
        onClearFocusedTable?.();
      }
    }
  }, [focusedTableId, tables, onClearFocusedTable]);

  // Masa seçimi
  const handleSelectTable = (table: TableFloorStatusDto) => {
    setSelectedTable(table);
    setIsDetailDrawerOpen(true);
  };

  const handleCloseDetailDrawer = () => {
    setIsDetailDrawerOpen(false);
  };

  // Taşı / Birleştir modalını açma
  const handleOpenMovementModal = (source: TableFloorStatusDto) => {
    setMovementSourceTable(source);
    setIsMovementModalOpen(true);
  };

  const handleCloseMovementModal = () => {
    setIsMovementModalOpen(false);
    setMovementSourceTable(null);
  };

  if (isLoading && tables.length === 0) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', minHeight: '320px', gap: '1rem' }}>
        <Spinner size="lg" />
        <p style={{ color: 'var(--ro-color-text-secondary, #6b7280)', margin: 0 }}>Kat planı ve masalar yükleniyor...</p>
      </div>
    );
  }

  if (errorMessage && tables.length === 0) {
    return (
      <ErrorState
        title="Kat Planı Yüklenemedi"
        message={errorMessage}
        retryLabel="Tekrar Dene"
        onRetry={() => loadFloorData(false)}
      />
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
      {/* Aktif Çağrılar & Hazır Ürünler Hızlı Çubuğu */}
      {(liveEventCount > 0 || readyItemsCount > 0) && onOpenCallsModal && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '10px 14px',
            backgroundColor: readyItemsCount > 0 ? '#f0fdf4' : 'var(--ro-color-warning-subtle, #fef3c7)',
            border: `1px solid ${readyItemsCount > 0 ? '#bbf7d0' : 'var(--ro-color-warning-border, #fcd34d)'}`,
            borderRadius: 'var(--ro-radius-md, 8px)',
          }}
        >
          <span style={{ fontSize: '0.9rem', fontWeight: 600, color: readyItemsCount > 0 ? '#166534' : '#92400e' }}>
            {readyItemsCount > 0 ? '🍽️' : '🔔'}{' '}
            {terminalName ? `${terminalName}: ` : ''}
            {liveEventCount > 0 && `Bekleyen ${liveEventCount} servis çağrısı`}
            {liveEventCount > 0 && readyItemsCount > 0 && ' ve '}
            {readyItemsCount > 0 && `${readyItemsCount} hazır ürün servise hazır`}!
          </span>
          <Button variant="outline" size="sm" onClick={onOpenCallsModal} style={{ minHeight: '36px' }}>
            Görüntüle
          </Button>
        </div>
      )}

      {/* Bayat Veri / Çevrimdışı İkazı */}
      {isStaleData && (
        <div
          role="status"
          style={{
            padding: '8px 14px',
            backgroundColor: '#fffbeb',
            border: '1px solid #fef08a',
            borderRadius: '6px',
            color: '#854d0e',
            fontSize: '0.85rem',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
          }}
        >
          <span>⚠️ Sunucuyla bağlantı kurulamadı. Ekrandaki masa verileri güncel olmayabilir.</span>
          <Button variant="outline" size="sm" onClick={() => loadFloorData(false)} style={{ minHeight: '36px' }}>
            Yenile
          </Button>
        </div>
      )}

      {/* Masalar Hiç Yoksa */}
      {tables.length === 0 ? (
        <EmptyState
          title="Tanımlı Masa Bulunamadı"
          description="Bu şubede henüz kayıtlı masa bulunmamaktadır. Masa tanımlamalarını restoran yönetim panelinden yapabilirsiniz."
          actionLabel="Yenile"
          onAction={() => loadFloorData(false)}
        />
      ) : (
        /* Kat Planı ve Izgara Arayüzü */
        <FloorPlanView
          tables={tables}
          diningAreas={diningAreas}
          selectedTableId={selectedTable?.table.id}
          onSelectTable={handleSelectTable}
          isLoading={isLoading}
        />
      )}

      {/* WAIT-03: Masa Oturumu Detay Çekmecesi */}
      <SessionDetailDrawer
        isOpen={isDetailDrawerOpen}
        onClose={handleCloseDetailDrawer}
        branchId={branchId}
        tableFloorDto={selectedTable}
        onRefreshFloor={() => loadFloorData(true)}
        onOpenMovementModal={handleOpenMovementModal}
      />

      {/* WAIT-05: Masa Taşıma ve Birleştirme Modalı */}
      <SessionMovementModal
        isOpen={isMovementModalOpen}
        onClose={handleCloseMovementModal}
        branchId={branchId}
        sourceTable={movementSourceTable}
        allTables={tables}
        onSuccess={() => loadFloorData(true)}
      />
    </div>
  );
};
