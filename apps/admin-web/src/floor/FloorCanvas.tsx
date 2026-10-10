import React, { useState, useRef, useCallback } from 'react';
import { Button } from '@restaurant-order/ui';
import type {
  RestaurantTableDto,
  TableFloorStatusDto,
  TableLayoutBatchItem,
} from '@restaurant-order/contracts';
import { TableInspector, LocalTableLayout } from './TableInspector';

export interface FloorCanvasProps {
  tables: RestaurantTableDto[];
  statuses: TableFloorStatusDto[];
  selectedTableId: string | null;
  onSelectTable: (table: RestaurantTableDto | null) => void;
  onSaveBatchLayout: (items: TableLayoutBatchItem[]) => Promise<void>;
  isSaving: boolean;
  onEditTableDetails: (table: RestaurantTableDto) => void;
}


const GRID_SNAP = 10;
const CANVAS_WIDTH = 1000;
const CANVAS_HEIGHT = 700;

function snap(val: number): number {
  return Math.round(val / GRID_SNAP) * GRID_SNAP;
}

export const FloorCanvas: React.FC<FloorCanvasProps> = ({
  tables,
  statuses,
  selectedTableId,
  onSelectTable,
  onSaveBatchLayout,
  isSaving,
  onEditTableDetails,
}) => {
  // Local modifications map: tableId -> LocalTableLayout
  const [localLayouts, setLocalLayouts] = useState<Record<string, LocalTableLayout>>({});
  const [dragState, setDragState] = useState<{
    tableId: string;
    startX: number;
    startY: number;
    origX: number;
    origY: number;
  } | null>(null);

  const svgRef = useRef<SVGSVGElement | null>(null);

  const hasUnsavedChanges = Object.keys(localLayouts).length > 0;

  const getTableLayout = useCallback(
    (table: RestaurantTableDto): LocalTableLayout => {
      const local = localLayouts[table.id];
      if (local) return local;
      return {
        positionX: table.positionX,
        positionY: table.positionY,
        width: table.width,
        height: table.height,
        rotationDegrees: table.rotationDegrees,
        shape: table.shape,
      };
    },
    [localLayouts]
  );

  const updateTableLayoutField = (tableId: string, updates: Partial<LocalTableLayout>) => {
    const table = tables.find((t) => t.id === tableId);
    if (!table) return;
    const current = getTableLayout(table);
    const updated: LocalTableLayout = { ...current, ...updates };

    // Check if reverted to original server values
    if (
      updated.positionX === table.positionX &&
      updated.positionY === table.positionY &&
      updated.width === table.width &&
      updated.height === table.height &&
      updated.rotationDegrees === table.rotationDegrees &&
      updated.shape === table.shape
    ) {
      setLocalLayouts((prev) => {
        const next = { ...prev };
        delete next[tableId];
        return next;
      });
    } else {
      setLocalLayouts((prev) => ({ ...prev, [tableId]: updated }));
    }
  };

  const handlePointerDown = (table: RestaurantTableDto, e: React.PointerEvent) => {
    try {
      e.currentTarget.setPointerCapture(e.pointerId);
    } catch {
      // Ignored in test / non-supporting environments
    }
    onSelectTable(table);
    const current = getTableLayout(table);
    setDragState({
      tableId: table.id,
      startX: e.clientX,
      startY: e.clientY,
      origX: current.positionX,
      origY: current.positionY,
    });
  };

  const handlePointerMove = (e: React.PointerEvent) => {
    if (!dragState) return;
    const dx = e.clientX - dragState.startX;
    const dy = e.clientY - dragState.startY;
    const newX = Math.max(0, Math.min(CANVAS_WIDTH - 80, snap(dragState.origX + dx)));
    const newY = Math.max(0, Math.min(CANVAS_HEIGHT - 80, snap(dragState.origY + dy)));
    updateTableLayoutField(dragState.tableId, { positionX: newX, positionY: newY });
  };

  const handlePointerUp = (e: React.PointerEvent) => {
    if (dragState) {
      try {
        e.currentTarget.releasePointerCapture(e.pointerId);
      } catch {
        // Ignored
      }
      setDragState(null);
    }
  };

  const handleKeyDown = (table: RestaurantTableDto, e: React.KeyboardEvent) => {
    const step = e.shiftKey ? 50 : 10;
    const current = getTableLayout(table);
    let handled = false;

    if (e.key === 'ArrowLeft') {
      updateTableLayoutField(table.id, { positionX: Math.max(0, current.positionX - step) });
      handled = true;
    } else if (e.key === 'ArrowRight') {
      updateTableLayoutField(table.id, {
        positionX: Math.min(CANVAS_WIDTH - current.width, current.positionX + step),
      });
      handled = true;
    } else if (e.key === 'ArrowUp') {
      updateTableLayoutField(table.id, { positionY: Math.max(0, current.positionY - step) });
      handled = true;
    } else if (e.key === 'ArrowDown') {
      updateTableLayoutField(table.id, {
        positionY: Math.min(CANVAS_HEIGHT - current.height, current.positionY + step),
      });
      handled = true;
    } else if (e.key === 'r' || e.key === 'R') {
      const nextRot = (current.rotationDegrees + 45) % 360;
      updateTableLayoutField(table.id, { rotationDegrees: nextRot });
      handled = true;
    } else if (e.key === 'Enter' || e.key === ' ') {
      onSelectTable(table);
      handled = true;
    }

    if (handled) {
      e.preventDefault();
    }
  };

  const handleSaveAll = async () => {
    const items: TableLayoutBatchItem[] = Object.entries(localLayouts).map(([tableId, layout]) => {
      const table = tables.find((t) => t.id === tableId)!;
      return {
        tableId,
        positionX: layout.positionX,
        positionY: layout.positionY,
        width: layout.width,
        height: layout.height,
        rotationDegrees: layout.rotationDegrees,
        shape: layout.shape,
        concurrencyToken: table.concurrencyToken,
      };
    });

    await onSaveBatchLayout(items);
    setLocalLayouts({});
  };

  const handleDiscardChanges = () => {
    setLocalLayouts({});
  };

  const selectedTable = tables.find((t) => t.id === selectedTableId) || null;
  const selectedLayout = selectedTable ? getTableLayout(selectedTable) : null;

  return (
    <div className="floor-container" data-testid="floor-canvas-container">
      {hasUnsavedChanges && (
        <div className="floor-unsaved-banner" role="status" aria-live="polite">
          <p className="floor-unsaved-text">
            {Object.keys(localLayouts).length} masanın yerleşiminde kaydedilmemiş değişiklikler var.
          </p>
          <div className="floor-unsaved-actions">
            <Button
              variant="outline"
              size="sm"
              onClick={handleDiscardChanges}
              disabled={isSaving}
              style={{ color: '#ffffff', borderColor: '#757575' }}
            >
              Geri Al
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={handleSaveAll}
              disabled={isSaving}
              data-testid="save-batch-layout-btn"
            >
              {isSaving ? 'Kaydediliyor...' : 'Tüm Değişiklikleri Kaydet (Atomic Save)'}
            </Button>
          </div>
        </div>
      )}

      {/* SVG Canvas Area */}
      <div
        className="floor-canvas-wrapper"
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
      >
        <svg
          ref={svgRef}
          className="floor-svg-canvas"
          viewBox={`0 0 ${CANVAS_WIDTH} ${CANVAS_HEIGHT}`}
          role="region"
          aria-label="Masa yerleşim planı"
          data-testid="floor-canvas-svg"
        >
          {/* Subtle Grid Lines */}
          <defs>
            <pattern id="floor-grid" width="40" height="40" patternUnits="userSpaceOnUse">
              <path d="M 40 0 L 0 0 0 40" fill="none" stroke="#e0e0e0" strokeWidth="0.8" />
            </pattern>
          </defs>
          <rect width={CANVAS_WIDTH} height={CANVAS_HEIGHT} fill="url(#floor-grid)" />

          {tables.map((table) => {
            const layout = getTableLayout(table);
            const isSelected = table.id === selectedTableId;
            const statusEntry = statuses.find((s) => s.table?.id === table.id);
            const activeSession = statusEntry?.activeSession;
            const sessionStatus = activeSession?.status;

            let badgeFill = '#e0e0e0';
            let badgeText = 'Boş';
            if (!table.isActive) {
              badgeFill = '#9e9e9e';
              badgeText = 'Pasif';
            } else if (sessionStatus === 'Open') {
              badgeFill = '#b0bec5';
              badgeText = 'Açık';
            } else if (sessionStatus === 'Active') {
              badgeFill = '#212121';
              badgeText = 'Aktif';
            } else if (sessionStatus === 'BillRequested') {
              badgeFill = '#616161';
              badgeText = 'Hesap';
            }

            const cx = layout.positionX + layout.width / 2;
            const cy = layout.positionY + layout.height / 2;
            const transform = `rotate(${layout.rotationDegrees} ${cx} ${cy})`;

            return (
              <g
                key={table.id}
                className={`floor-table-node ${isSelected ? 'selected' : ''} ${
                  !table.isActive ? 'inactive' : ''
                }`}
                tabIndex={0}
                role="button"
                aria-label={`Masa ${table.tableNumber}, ${table.name}, Kapasite: ${table.capacity}, Durum: ${badgeText}`}
                transform={transform}
                onClick={() => onSelectTable(table)}
                onPointerDown={(e) => handlePointerDown(table, e)}
                onKeyDown={(e) => handleKeyDown(table, e)}
                data-testid={`floor-table-${table.id}`}
              >
                {layout.shape === 'Round' ? (
                  <circle
                    cx={cx}
                    cy={cy}
                    r={Math.min(layout.width, layout.height) / 2}
                    className="table-shape-round"
                  />
                ) : (
                  <rect
                    x={layout.positionX}
                    y={layout.positionY}
                    width={layout.width}
                    height={layout.height}
                    rx="8"
                    ry="8"
                    className="table-shape-square"
                  />
                )}

                {/* Table Number */}
                <text x={cx} y={cy - 10} className="table-label-number">
                  {table.tableNumber}
                </text>

                {/* Table Name */}
                <text x={cx} y={cy + 8} className="table-label-name">
                  {table.name} ({table.capacity}p)
                </text>

                {/* Status Badge */}
                <rect
                  x={cx - 24}
                  y={cy + 18}
                  width="48"
                  height="16"
                  rx="4"
                  ry="4"
                  fill={badgeFill}
                />
                <text
                  x={cx}
                  y={cy + 26}
                  fill={sessionStatus === 'Active' ? '#ffffff' : '#111111'}
                  className="table-badge-text"
                >
                  {badgeText}
                </text>
              </g>
            );
          })}
        </svg>
      </div>

      {/* Selected Table Inspector & Fine-grained Accessible Controls */}
      {selectedTable && selectedLayout && (
        <TableInspector
          table={selectedTable}
          layout={selectedLayout}
          onUpdateLayout={(updates) => updateTableLayoutField(selectedTable.id, updates)}
          onEditDetails={onEditTableDetails}
          onClose={() => onSelectTable(null)}
        />
      )}
    </div>
  );
};
