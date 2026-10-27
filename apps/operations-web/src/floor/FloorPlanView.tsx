import React, { useState, useMemo } from 'react';
import {
  Card,
  Button,
  Badge,
  Input,
  Select,
} from '@restaurant-order/ui';
import { TableFloorStatusDto, DiningAreaContract } from '@restaurant-order/contracts';
import {
  resolveTableOperationalStatus,
  TABLE_STATUS_VISUALS,
  TableOperationalStatus,
} from './types';

export interface FloorPlanViewProps {
  tables: TableFloorStatusDto[];
  diningAreas: DiningAreaContract[];
  selectedTableId?: string | null;
  onSelectTable: (table: TableFloorStatusDto) => void;
  isLoading?: boolean;
}

export const FloorPlanView: React.FC<FloorPlanViewProps> = ({
  tables,
  diningAreas,
  selectedTableId,
  onSelectTable,
  isLoading = false,
}) => {
  const [selectedAreaId, setSelectedAreaId] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [viewMode, setViewMode] = useState<'grid' | 'canvas'>('grid');

  // Filtreleme
  const filteredTables = useMemo(() => {
    return tables.filter((t) => {
      // Alan filtresi
      if (selectedAreaId !== 'all' && t.table.diningAreaId !== selectedAreaId) {
        return false;
      }
      // Arama filtresi
      if (searchQuery.trim()) {
        const query = searchQuery.trim().toLowerCase();
        const matchesNumber = t.table.tableNumber.toLowerCase().includes(query);
        const matchesName = t.table.name.toLowerCase().includes(query);
        return matchesNumber || matchesName;
      }
      return true;
    });
  }, [tables, selectedAreaId, searchQuery]);

  // Durum sayıları özeti
  const statusCounts = useMemo(() => {
    const counts: Record<TableOperationalStatus, number> = {
      Available: 0,
      Seated: 0,
      OrderPending: 0,
      ReadyToServe: 0,
      BillRequested: 0,
      Inactive: 0,
    };
    tables.forEach((t) => {
      const status = resolveTableOperationalStatus(t);
      counts[status] = (counts[status] || 0) + 1;
    });
    return counts;
  }, [tables]);

  const areaOptions = useMemo(() => {
    return [
      { value: 'all', label: `Tüm Alanlar (${tables.length})` },
      ...diningAreas.map((a) => {
        const areaTableCount = tables.filter((t) => t.table.diningAreaId === a.id).length;
        return { value: a.id, label: `${a.name} (${areaTableCount})` };
      }),
    ];
  }, [diningAreas, tables]);

  return (
    <div className="floor-plan-container" style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
      {/* Üst Filtre ve Araç Çubuğu */}
      <Card padding="sm" style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
        <div
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: '0.5rem',
          }}
        >
          <div style={{ display: 'flex', gap: '0.5rem', flex: '1 1 200px', minWidth: '180px' }}>
            <Input
              type="search"
              placeholder="Masa ara (örn. T-1, 10)..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              aria-label="Masa arama"
              style={{ width: '100%', minHeight: '44px' }}
            />
          </div>

          <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
            <Select
              value={selectedAreaId}
              onChange={(e) => setSelectedAreaId(e.target.value)}
              options={areaOptions}
              aria-label="Yemek Alanı Filtresi"
              style={{ minHeight: '44px', minWidth: '150px' }}
            />

            <div style={{ display: 'flex', gap: '4px' }}>
              <Button
                variant={viewMode === 'grid' ? 'primary' : 'outline'}
                size="sm"
                onClick={() => setViewMode('grid')}
                aria-label="Izgara Görünümü"
                style={{ minHeight: '44px', minWidth: '44px' }}
                title="Izgara Görünümü"
              >
                ⊞ Izgara
              </Button>
              <Button
                variant={viewMode === 'canvas' ? 'primary' : 'outline'}
                size="sm"
                onClick={() => setViewMode('canvas')}
                aria-label="Kat Planı Görünümü"
                style={{ minHeight: '44px', minWidth: '44px' }}
                title="Kat Planı Görünümü"
              >
                🗺️ Kroki
              </Button>
            </div>
          </div>
        </div>

        {/* Durum Sayaçları Şeridi */}
        <div
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            gap: '0.5rem',
            fontSize: '0.85rem',
            paddingTop: '0.25rem',
          }}
        >
          {(Object.keys(TABLE_STATUS_VISUALS) as TableOperationalStatus[]).map((st) => {
            const visual = TABLE_STATUS_VISUALS[st];
            const count = statusCounts[st] || 0;
            if (count === 0 && st === 'Inactive') return null;
            return (
              <span
                key={st}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '4px',
                  padding: '2px 8px',
                  borderRadius: '12px',
                  backgroundColor: 'var(--ro-color-surface-hover, #f3f4f6)',
                  border: `1px solid ${visual.color}40`,
                }}
              >
                <span aria-hidden="true">{visual.icon}</span>
                <span>{visual.label}:</span>
                <strong>{count}</strong>
              </span>
            );
          })}
        </div>
      </Card>

      {/* İçerik: Izgara veya Kroki */}
      {filteredTables.length === 0 ? (
        <Card padding="md" style={{ textAlign: 'center', minHeight: '180px', display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center' }}>
          <p style={{ margin: 0, fontWeight: 600, fontSize: '1.1rem' }}>Eşleşen Masa Bulunamadı</p>
          <p style={{ margin: '0.25rem 0 0', color: 'var(--ro-color-text-secondary, #6b7280)' }}>
            Seçili alan veya arama filtresine uygun masa mevcut değil.
          </p>
        </Card>
      ) : viewMode === 'grid' ? (
        /* Izgara Görünümü */
        <div
          role="grid"
          aria-label="Masa Listesi"
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(140px, 1fr))',
            gap: '0.75rem',
          }}
        >
          {filteredTables.map((tableDto) => {
            const status = resolveTableOperationalStatus(tableDto);
            const visual = TABLE_STATUS_VISUALS[status];
            const isSelected = selectedTableId === tableDto.table.id;

            return (
              <button
                key={tableDto.table.id}
                type="button"
                onClick={() => onSelectTable(tableDto)}
                disabled={isLoading}
                aria-label={`${tableDto.table.tableNumber} - ${tableDto.table.name}, Durum: ${visual.label}, Kapasite: ${tableDto.table.capacity} kişi`}
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  justifyContent: 'space-between',
                  padding: '12px',
                  minHeight: '120px',
                  borderRadius: '10px',
                  border: isSelected
                    ? '3px solid var(--ro-color-primary, #2563eb)'
                    : `2px solid ${visual.color}`,
                  backgroundColor: isSelected
                    ? 'var(--ro-color-primary-subtle)'
                    : 'var(--ro-color-surface)',
                  cursor: 'pointer',
                  textAlign: 'left',
                  boxShadow: isSelected
                    ? '0 0 0 2px rgba(37, 99, 235, 0.2)'
                    : '0 1px 3px rgba(0,0,0,0.05)',
                  transition: 'transform 0.1s ease, box-shadow 0.1s ease',
                  outline: 'none',
                  minWidth: '44px',
                }}
              >
                {/* Üst Kısım: Masa No ve Rozetler */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', width: '100%' }}>
                  <div>
                    <span style={{ fontSize: '1.25rem', fontWeight: 700, lineHeight: 1.1 }}>
                      {tableDto.table.tableNumber}
                    </span>
                    <span
                      style={{
                        display: 'block',
                        fontSize: '0.75rem',
                        color: 'var(--ro-color-text-secondary, #6b7280)',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                        maxWidth: '90px',
                      }}
                    >
                      {tableDto.table.name}
                    </span>
                  </div>

                  {/* Çağrı ve Hazır Uyarı Rozetleri */}
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', alignItems: 'flex-end' }}>
                    {(tableDto.activeServiceRequestsCount ?? 0) > 0 && (
                      <span
                        title={`${tableDto.activeServiceRequestsCount} bekleyen servis çağrısı`}
                        style={{
                          backgroundColor: '#ef4444',
                          color: '#ffffff',
                          borderRadius: '10px',
                          padding: '1px 5px',
                          fontSize: '0.7rem',
                          fontWeight: 700,
                        }}
                      >
                        🔔 {tableDto.activeServiceRequestsCount}
                      </span>
                    )}
                    {(tableDto.readyItemsCount ?? 0) > 0 && (
                      <span
                        title={`${tableDto.readyItemsCount} servise hazır ürün`}
                        style={{
                          backgroundColor: '#8b5cf6',
                          color: '#ffffff',
                          borderRadius: '10px',
                          padding: '1px 5px',
                          fontSize: '0.7rem',
                          fontWeight: 700,
                        }}
                      >
                        🍽️ {tableDto.readyItemsCount}
                      </span>
                    )}
                  </div>
                </div>

                {/* Alt Kısım: Durum Rozeti ve Kapasite */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '8px' }}>
                  <Badge variant={visual.badgeVariant}>
                    <span aria-hidden="true" style={{ marginRight: '3px' }}>{visual.icon}</span>
                    {visual.label}
                  </Badge>
                  <span style={{ fontSize: '0.75rem', color: 'var(--ro-color-text-secondary, #6b7280)' }}>
                    👥 {tableDto.table.capacity}
                  </span>
                </div>
              </button>
            );
          })}
        </div>
      ) : (
        /* Kat Planı (Kroki/Canvas) Görünümü */
        <div
          role="region"
          aria-label="Etkileşimli Kroki Kat Planı"
          style={{
            position: 'relative',
            width: '100%',
            height: '480px',
            overflow: 'auto',
            borderRadius: '8px',
            border: '1px solid var(--ro-color-border, #e5e7eb)',
            backgroundColor: '#f9fafb',
            padding: '16px',
          }}
        >
          {filteredTables.map((tableDto) => {
            const status = resolveTableOperationalStatus(tableDto);
            const visual = TABLE_STATUS_VISUALS[status];
            const isSelected = selectedTableId === tableDto.table.id;

            const posX = Math.max(0, tableDto.table.positionX || 0);
            const posY = Math.max(0, tableDto.table.positionY || 0);
            const width = Math.max(80, tableDto.table.width || 100);
            const height = Math.max(80, tableDto.table.height || 100);
            const isRound = tableDto.table.shape?.toLowerCase() === 'round';

            return (
              <button
                key={tableDto.table.id}
                type="button"
                onClick={() => onSelectTable(tableDto)}
                aria-label={`${tableDto.table.tableNumber} - ${visual.label}`}
                style={{
                  position: 'absolute',
                  left: `${posX}px`,
                  top: `${posY}px`,
                  width: `${width}px`,
                  height: `${height}px`,
                  borderRadius: isRound ? '50%' : '8px',
                  border: isSelected
                    ? '3px solid var(--ro-color-primary, #2563eb)'
                    : `2px solid ${visual.color}`,
                  backgroundColor: isSelected ? '#eff6ff' : '#ffffff',
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  cursor: 'pointer',
                  padding: '4px',
                  boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
                  zIndex: isSelected ? 10 : 1,
                  minWidth: '44px',
                  minHeight: '44px',
                }}
              >
                <div style={{ fontWeight: 700, fontSize: '0.95rem' }}>{tableDto.table.tableNumber}</div>
                <div style={{ fontSize: '0.7rem', color: visual.color, fontWeight: 600 }}>{visual.label}</div>
                {(tableDto.activeServiceRequestsCount ?? 0) > 0 && (
                  <span style={{ fontSize: '0.7rem', color: '#ef4444' }}>
                    🔔 {tableDto.activeServiceRequestsCount}
                  </span>
                )}
                {(tableDto.readyItemsCount ?? 0) > 0 && (
                  <span style={{ fontSize: '0.7rem', color: '#8b5cf6' }}>
                    🍽️ {tableDto.readyItemsCount}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
};
