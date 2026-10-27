import React, { useState, useMemo } from 'react';
import { RuntimeMenuItem, RuntimeMenuCategory } from '@restaurant-order/contracts';

export interface StaffOrderItemsGridProps {
  categories: RuntimeMenuCategory[];
  loading: boolean;
  error: string | null;
  onRetry: () => void;
  onSelectItem: (item: RuntimeMenuItem) => void;
}

export const StaffOrderItemsGrid: React.FC<StaffOrderItemsGridProps> = ({
  categories,
  loading,
  error,
  onRetry,
  onSelectItem,
}) => {
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);

  const displayedItems = useMemo(() => {
    let items: RuntimeMenuItem[] = [];
    if (selectedCategoryId) {
      const cat = categories.find((c) => c.id === selectedCategoryId);
      if (cat) items = cat.items;
    } else {
      items = categories.flatMap((c) => c.items);
    }

    if (!searchQuery.trim()) return items;

    const query = searchQuery.trim().toLocaleLowerCase('tr-TR');
    return items.filter((item) => {
      const nameMatch = item.name.toLocaleLowerCase('tr-TR').includes(query);
      const descMatch = (item.shortDescription || '').toLocaleLowerCase('tr-TR').includes(query);
      return nameMatch || descMatch;
    });
  }, [categories, selectedCategoryId, searchQuery]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
      {/* Search & Categories Bar */}
      <div style={{ padding: '12px 16px', borderBottom: '1px solid var(--ro-color-border, #e5e7eb)' }}>
        <input
          type="search"
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          placeholder="Ürün adı veya açıklama ile ara..."
          style={{
            width: '100%',
            padding: '8px 12px',
            borderRadius: '8px',
            border: '1px solid var(--ro-color-border, #e5e7eb)',
            fontSize: '14px',
            boxSizing: 'border-box',
            marginBottom: '10px',
          }}
        />

        <div style={{ display: 'flex', gap: '8px', overflowX: 'auto', paddingBottom: '4px' }}>
          <button
            type="button"
            onClick={() => setSelectedCategoryId(null)}
            style={{
              padding: '6px 12px',
              borderRadius: '20px',
              border: '1px solid var(--ro-color-border, #e5e7eb)',
              backgroundColor: selectedCategoryId === null ? 'var(--ro-color-primary, #2563eb)' : 'transparent',
              color: selectedCategoryId === null ? '#ffffff' : 'inherit',
              fontSize: '12px',
              fontWeight: 500,
              cursor: 'pointer',
              whiteSpace: 'nowrap',
            }}
          >
            Tüm Ürünler
          </button>
          {categories.map((cat) => {
            const isSelected = selectedCategoryId === cat.id;
            return (
              <button
                key={cat.id}
                type="button"
                onClick={() => setSelectedCategoryId(cat.id)}
                style={{
                  padding: '6px 12px',
                  borderRadius: '20px',
                  border: '1px solid var(--ro-color-border, #e5e7eb)',
                  backgroundColor: isSelected ? 'var(--ro-color-primary, #2563eb)' : 'transparent',
                  color: isSelected ? '#ffffff' : 'inherit',
                  fontSize: '12px',
                  fontWeight: 500,
                  cursor: 'pointer',
                  whiteSpace: 'nowrap',
                }}
              >
                {cat.name}
              </button>
            );
          })}
        </div>
      </div>

      {/* Grid */}
      <div style={{ flex: 1, overflowY: 'auto', padding: '16px' }}>
        {loading && (
          <div style={{ textAlign: 'center', padding: '40px', color: 'var(--ro-color-text-muted, #6b7280)' }}>
            Menü yükleniyor...
          </div>
        )}
        {error && (
          <div style={{ textAlign: 'center', padding: '24px', color: 'var(--ro-color-error, #dc2626)' }}>
            <p>{error}</p>
            <button
              type="button"
              onClick={onRetry}
              style={{
                padding: '8px 16px',
                borderRadius: '6px',
                backgroundColor: 'var(--ro-color-primary, #2563eb)',
                color: '#ffffff',
                border: 'none',
                cursor: 'pointer',
              }}
            >
              Tekrar Dene
            </button>
          </div>
        )}

        {!loading && !error && (
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
              gap: '12px',
            }}
          >
            {displayedItems.map((item) => {
              const isAvailable = item.isAvailable;
              return (
                <button
                  key={item.id}
                  type="button"
                  disabled={!isAvailable}
                  onClick={() => onSelectItem(item)}
                  style={{
                    textAlign: 'left',
                    padding: '12px',
                    borderRadius: '8px',
                    border: '1px solid var(--ro-color-border, #e5e7eb)',
                    backgroundColor: 'var(--ro-color-surface, #ffffff)',
                    opacity: isAvailable ? 1 : 0.45,
                    cursor: isAvailable ? 'pointer' : 'not-allowed',
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'space-between',
                    height: '110px',
                    boxSizing: 'border-box',
                  }}
                >
                  <div>
                    <div style={{ fontWeight: 600, fontSize: '14px', lineHeight: 1.2 }}>{item.name}</div>
                    {item.shortDescription && (
                      <div
                        style={{
                          fontSize: '11px',
                          color: 'var(--ro-color-text-muted, #6b7280)',
                          marginTop: '4px',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                        }}
                      >
                        {item.shortDescription}
                      </div>
                    )}
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '8px' }}>
                    <span style={{ fontWeight: 700, fontSize: '13px', color: 'var(--ro-color-primary, #2563eb)' }}>
                      ₺{(item.basePriceMinorUnits / 100).toFixed(2)}
                    </span>
                    {!isAvailable && (
                      <span style={{ fontSize: '10px', backgroundColor: '#fee2e2', color: '#b91c1c', padding: '2px 4px', borderRadius: '4px' }}>
                        Tükendi
                      </span>
                    )}
                  </div>
                </button>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
};
