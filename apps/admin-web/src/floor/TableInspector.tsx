import React from 'react';
import { Card, Button } from '@restaurant-order/ui';
import type { RestaurantTableDto, TableShape } from '@restaurant-order/contracts';

export interface LocalTableLayout {
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  rotationDegrees: number;
  shape: TableShape;
}

export interface TableInspectorProps {
  table: RestaurantTableDto;
  layout: LocalTableLayout;
  onUpdateLayout: (updates: Partial<LocalTableLayout>) => void;
  onEditDetails: (table: RestaurantTableDto) => void;
  onClose: () => void;
}

export const TableInspector: React.FC<TableInspectorProps> = ({
  table,
  layout,
  onUpdateLayout,
  onEditDetails,
  onClose,
}) => {
  return (
    <Card padding="md">
      <div className="floor-inspector-panel" data-testid="table-inspector">
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            flexWrap: 'wrap',
            gap: 'var(--ro-space-2)',
          }}
        >
          <div>
            <h3 style={{ margin: 0, fontSize: 'var(--ro-font-size-md)', fontWeight: 700 }}>
              Masa {table.tableNumber}: {table.name}
            </h3>
            <p
              style={{
                margin: '2px 0 0 0',
                fontSize: 'var(--ro-font-size-xs)',
                color: 'var(--ro-color-text-muted)',
              }}
            >
              Kapasite: {table.capacity} kişi • Şekil: {layout.shape} • QR Sürüm: v{table.qrVersion}
            </p>
          </div>

          <div style={{ display: 'flex', gap: 'var(--ro-space-2)' }}>
            <Button
              variant="outline"
              size="sm"
              onClick={() => onEditDetails(table)}
              data-testid="edit-table-details-btn"
            >
              Masa Bilgilerini Düzenle
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={onClose}
              aria-label="Seçimi kaldır"
            >
              Kapat
            </Button>
          </div>
        </div>

        {/* Numeric and Accessible Position Controls */}
        <div className="floor-coords-grid">
          <label>
            <span style={{ fontSize: '11px', fontWeight: 600, color: '#616161' }}>X Koordinatı</span>
            <input
              type="number"
              className="floor-input-control"
              aria-label="Masa X Koordinatı"
              value={layout.positionX}
              onChange={(e) =>
                onUpdateLayout({
                  positionX: Math.max(0, parseInt(e.target.value, 10) || 0),
                })
              }
            />
          </label>

          <label>
            <span style={{ fontSize: '11px', fontWeight: 600, color: '#616161' }}>Y Koordinatı</span>
            <input
              type="number"
              className="floor-input-control"
              aria-label="Masa Y Koordinatı"
              value={layout.positionY}
              onChange={(e) =>
                onUpdateLayout({
                  positionY: Math.max(0, parseInt(e.target.value, 10) || 0),
                })
              }
            />
          </label>

          <label>
            <span style={{ fontSize: '11px', fontWeight: 600, color: '#616161' }}>Genişlik (px)</span>
            <input
              type="number"
              className="floor-input-control"
              aria-label="Masa Genişliği"
              value={layout.width}
              onChange={(e) =>
                onUpdateLayout({
                  width: Math.max(40, parseInt(e.target.value, 10) || 40),
                })
              }
            />
          </label>

          <label>
            <span style={{ fontSize: '11px', fontWeight: 600, color: '#616161' }}>Yükseklik (px)</span>
            <input
              type="number"
              className="floor-input-control"
              aria-label="Masa Yüksekliği"
              value={layout.height}
              onChange={(e) =>
                onUpdateLayout({
                  height: Math.max(40, parseInt(e.target.value, 10) || 40),
                })
              }
            />
          </label>

          <label>
            <span style={{ fontSize: '11px', fontWeight: 600, color: '#616161' }}>Döndürme (Derece)</span>
            <select
              className="floor-input-control"
              aria-label="Masa Döndürme Derecesi"
              value={layout.rotationDegrees}
              onChange={(e) =>
                onUpdateLayout({
                  rotationDegrees: parseInt(e.target.value, 10) || 0,
                })
              }
            >
              <option value={0}>0°</option>
              <option value={45}>45°</option>
              <option value={90}>90°</option>
              <option value={180}>180°</option>
              <option value={270}>270°</option>
            </select>
          </label>

          <label>
            <span style={{ fontSize: '11px', fontWeight: 600, color: '#616161' }}>Şekil</span>
            <select
              className="floor-input-control"
              aria-label="Masa Şekli"
              value={layout.shape}
              onChange={(e) =>
                onUpdateLayout({
                  shape: e.target.value as TableShape,
                })
              }
            >
              <option value="Square">Kare</option>
              <option value="Rectangle">Dikdörtgen</option>
              <option value="Round">Yuvarlak</option>
            </select>
          </label>
        </div>
      </div>
    </Card>
  );
};
