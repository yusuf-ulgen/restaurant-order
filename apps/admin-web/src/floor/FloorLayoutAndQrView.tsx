import React, { useState, useEffect, useCallback } from 'react';
import {
  Card,
  Button,
  Spinner,
  EmptyState,
  PageHeader,
} from '@restaurant-order/ui';
import type {
  RestaurantTableDto,
  TableFloorStatusDto,
  DiningAreaContract,
  CreateTableRequest,
  UpdateTableRequest,
  TableLayoutBatchItem,
} from '@restaurant-order/contracts';
import { useAdminConfig } from '../config/AdminConfigContext';
import { floorApi, FloorApiError } from './floorApi';
import { FloorCanvas } from './FloorCanvas';
import { TableEditorSheet } from './TableEditorSheet';
import { QrGeneratorView } from './QrGeneratorView';
import './floor.css';

export const FloorLayoutAndQrView: React.FC = () => {
  const { selectedBranchId, selectedBranch } = useAdminConfig();

  // Mode: layout canvas vs QR generator
  const [activeTab, setActiveTab] = useState<'layout' | 'qr'>('layout');

  // Dining Areas and Tables state
  const [diningAreas, setDiningAreas] = useState<DiningAreaContract[]>([]);
  const [selectedAreaId, setSelectedAreaId] = useState<string>('');
  const [tables, setTables] = useState<RestaurantTableDto[]>([]);
  const [floorStatuses, setFloorStatuses] = useState<TableFloorStatusDto[]>([]);
  const [selectedTable, setSelectedTable] = useState<RestaurantTableDto | null>(null);

  // Loading & Error states
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [conflictError, setConflictError] = useState<string | null>(null);

  // Editor sheet modal/bottom-sheet state
  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const [tableToEdit, setTableToEdit] = useState<RestaurantTableDto | null>(null);

  // Load dining areas
  const loadDiningAreas = useCallback(async (branchId: string) => {
    try {
      const areas = await floorApi.getDiningAreas(branchId);
      setDiningAreas(areas);
      if (areas.length > 0) {
        setSelectedAreaId((prev) => (areas.some((a) => a.id === prev) ? prev : areas[0]?.id || ''));
      } else {
        setSelectedAreaId('');
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Masa alanları yüklenemedi.';
      setErrorMessage(msg);
    }
  }, []);

  // Load tables & floor status
  const loadFloorData = useCallback(async (branchId: string, areaId?: string) => {
    setIsLoading(true);
    setErrorMessage(null);
    setConflictError(null);

    try {
      const [tableList, statusData] = await Promise.all([
        floorApi.getTables(branchId, areaId || undefined),
        floorApi.getFloorStatus(branchId).catch(() => ({ branchId, tables: [] })),
      ]);

      setTables(tableList);
      setFloorStatuses(statusData.tables || []);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Masa yerleşim verileri yüklenemedi.';
      setErrorMessage(msg);
    } finally {
      setIsLoading(false);
    }
  }, []);

  // Reset state and reload on branch switch
  useEffect(() => {
    if (!selectedBranchId) {
      setDiningAreas([]);
      setTables([]);
      setFloorStatuses([]);
      setSelectedTable(null);
      return;
    }

    loadDiningAreas(selectedBranchId);
  }, [selectedBranchId, loadDiningAreas]);

  // Reload tables when selected area changes
  useEffect(() => {
    if (selectedBranchId) {
      loadFloorData(selectedBranchId, selectedAreaId);
    }
  }, [selectedBranchId, selectedAreaId, loadFloorData]);

  // Handle Create Table
  const handleCreate = async (req: CreateTableRequest) => {
    if (!selectedBranchId) return;
    setIsSaving(true);
    try {
      const created = await floorApi.createTable(selectedBranchId, req);
      setTables((prev) => [...prev, created]);
      setSelectedTable(created);
    } finally {
      setIsSaving(false);
    }
  };

  // Handle Update Table Details
  const handleUpdate = async (tableId: string, req: UpdateTableRequest, etag: string) => {
    if (!selectedBranchId) return;
    setIsSaving(true);
    try {
      const updated = await floorApi.updateTable(selectedBranchId, tableId, req, etag);
      setTables((prev) => prev.map((t) => (t.id === tableId ? updated : t)));
      setSelectedTable(updated);
    } catch (err: unknown) {
      if (err instanceof FloorApiError && (err.isConflict || err.isPreconditionFailed)) {
        setConflictError(err.message);
      }
      throw err;
    } finally {
      setIsSaving(false);
    }
  };

  // Handle Toggle Active/Inactive
  const handleToggleActive = async (table: RestaurantTableDto) => {
    if (!selectedBranchId) return;
    setIsSaving(true);
    try {
      const updated = table.isActive
        ? await floorApi.deactivateTable(selectedBranchId, table.id, table.concurrencyToken)
        : await floorApi.activateTable(selectedBranchId, table.id, table.concurrencyToken);

      setTables((prev) => prev.map((t) => (t.id === table.id ? updated : t)));
      setSelectedTable(updated);
      setTableToEdit(updated);
    } catch (err: unknown) {
      if (err instanceof FloorApiError && (err.isConflict || err.isPreconditionFailed)) {
        setConflictError(err.message);
      }
      throw err;
    } finally {
      setIsSaving(false);
    }
  };

  // Handle Atomic Batch Layout Save
  const handleSaveBatchLayout = async (items: TableLayoutBatchItem[]) => {
    if (!selectedBranchId || items.length === 0) return;
    setIsSaving(true);
    setConflictError(null);

    try {
      const updatedList = await floorApi.batchUpdateLayout(selectedBranchId, items);
      setTables((prev) =>
        prev.map((t) => {
          const match = updatedList.find((u) => u.id === t.id);
          return match || t;
        })
      );
    } catch (err: unknown) {
      if (err instanceof FloorApiError && (err.isConflict || err.isPreconditionFailed)) {
        setConflictError('Yerleşim kaydedilirken çakışma oluştu. Lütfen sayfayı yenileyip tekrar deneyin.');
      } else {
        const msg = err instanceof Error ? err.message : 'Yerleşim kaydedilemedi.';
        setErrorMessage(msg);
      }
      throw err;
    } finally {
      setIsSaving(false);
    }
  };

  // Filtered tables for the current area
  const areaTables = selectedAreaId
    ? tables.filter((t) => t.diningAreaId === selectedAreaId)
    : tables;

  if (!selectedBranchId) {
    return (
      <Card padding="md">
        <EmptyState
          title="Şube Seçilmedi"
          description="Masa ve yerleşim planını görüntülemek için lütfen üst menüden bir şube seçin."
        />
      </Card>
    );
  }

  return (
    <div className="floor-container" data-testid="floor-layout-view">
      <PageHeader
        title="Masa Düzeni & QR Yönetimi"
        subtitle={`${selectedBranch?.name || 'Şube'} • Masa yerleşim planı, görsel koordinatlar ve masa QR kodları`}
        headingLevel={2}
        actions={
          <div style={{ display: 'flex', gap: 'var(--ro-space-2)' }}>
            <Button
              variant="outline"
              size="md"
              onClick={() => {
                setTableToEdit(null);
                setIsEditorOpen(true);
              }}
              data-testid="create-table-btn"
            >
              + Yeni Masa Ekle
            </Button>
          </div>
        }
      />

      {/* Mode Navigation Tabs */}
      <div className="floor-header-controls">
        <div className="floor-tabs">
          <button
            type="button"
            className={`floor-tab-btn ${activeTab === 'layout' ? 'active' : ''}`}
            onClick={() => setActiveTab('layout')}
            data-testid="tab-layout-btn"
          >
            Yerleşim Planı (Canvas)
          </button>
          <button
            type="button"
            className={`floor-tab-btn ${activeTab === 'qr' ? 'active' : ''}`}
            onClick={() => setActiveTab('qr')}
            data-testid="tab-qr-btn"
          >
            QR Kod Yönetimi & Baskı
          </button>
        </div>

        {/* Dining Area Selector Tabs (Only in Layout Mode) */}
        {activeTab === 'layout' && diningAreas.length > 0 && (
          <div className="floor-tabs" style={{ marginTop: 'var(--ro-space-2)' }}>
            {diningAreas.map((area) => (
              <button
                key={area.id}
                type="button"
                className={`floor-tab-btn ${selectedAreaId === area.id ? 'active' : ''}`}
                onClick={() => setSelectedAreaId(area.id)}
                data-testid={`area-tab-${area.id}`}
              >
                {area.name} ({tables.filter((t) => t.diningAreaId === area.id).length})
              </button>
            ))}
          </div>
        )}
      </div>

      {/* Conflict / Error Banner */}
      {(conflictError || errorMessage) && (
        <Card padding="md">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <p style={{ color: 'var(--ro-color-danger)', margin: 0, fontSize: 'var(--ro-font-size-sm)' }}>
              {conflictError || errorMessage}
            </p>
            <Button
              variant="outline"
              size="sm"
              onClick={() => loadFloorData(selectedBranchId, selectedAreaId)}
              data-testid="conflict-reload-btn"
            >
              Verileri Yenile
            </Button>
          </div>
        </Card>
      )}

      {/* Main Content Body */}
      {isLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: 'var(--ro-space-12)' }}>
          <Spinner size="lg" label="Masa yerleşim planı yükleniyor..." />
        </div>
      ) : activeTab === 'layout' ? (
        diningAreas.length === 0 ? (
          <Card padding="md">
            <EmptyState
              title="Tanımlı Masa Alanı Bulunamadı"
              description="Masa ekleyebilmek için önce 'Masa Alanları' ekranından bir salon veya alan oluşturmalısınız."
            />
          </Card>
        ) : areaTables.length === 0 ? (
          <Card padding="md">
            <EmptyState
              title="Bu Alanda Masa Bulunmuyor"
              description="Bu masa alanına henüz masa yerleştirilmemiştir. '+ Yeni Masa Ekle' butonuyla başlayabilirsiniz."
            />
          </Card>
        ) : (
          <FloorCanvas
            tables={areaTables}
            statuses={floorStatuses}
            selectedTableId={selectedTable?.id || null}
            onSelectTable={(tbl) => setSelectedTable(tbl)}
            onSaveBatchLayout={handleSaveBatchLayout}
            isSaving={isSaving}
            onEditTableDetails={(tbl) => {
              setTableToEdit(tbl);
              setIsEditorOpen(true);
            }}
          />
        )
      ) : (
        <QrGeneratorView
          branchId={selectedBranchId}
          branchName={selectedBranch?.name || 'Şube'}
          tables={tables}
          statuses={floorStatuses}
          onTableRotated={(updated) => {
            setTables((prev) => prev.map((t) => (t.id === updated.id ? updated : t)));
          }}
        />
      )}

      {/* Responsive Table Create/Edit Sheet */}
      <TableEditorSheet
        isOpen={isEditorOpen}
        onClose={() => {
          setIsEditorOpen(false);
          setTableToEdit(null);
        }}
        diningAreas={diningAreas}
        initialDiningAreaId={selectedAreaId || diningAreas[0]?.id || ''}
        tableToEdit={tableToEdit}
        onSaveCreate={handleCreate}
        onSaveUpdate={handleUpdate}
        onToggleActive={handleToggleActive}
        isSaving={isSaving}
      />
    </div>
  );
};
