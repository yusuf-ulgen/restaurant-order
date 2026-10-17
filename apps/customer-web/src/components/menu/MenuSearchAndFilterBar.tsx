import React from 'react';
import { Input, Button, Badge } from '@restaurant-order/ui';

export interface MenuSearchAndFilterBarProps {
  searchQuery: string;
  onSearchChange: (query: string) => void;
  activeFilterCount: number;
  onOpenFilters: () => void;
  onClearFilters: () => void;
  hasActiveFilters: boolean;
}

export const MenuSearchAndFilterBar: React.FC<MenuSearchAndFilterBarProps> = ({
  searchQuery,
  onSearchChange,
  activeFilterCount,
  onOpenFilters,
  onClearFilters,
  hasActiveFilters,
}) => {
  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--ro-space-2)',
        marginBottom: 'var(--ro-space-3)',
      }}
      data-testid="menu-search-and-filter-bar"
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--ro-space-2)',
        }}
      >
        <div style={{ flex: 1, position: 'relative' }}>
          <Input
            id="menu-search-input"
            type="search"
            aria-label="Menüde veya lezzetlerde ara"
            placeholder="Menüde veya lezzetlerde ara..."
            value={searchQuery}
            onChange={(e) => onSearchChange(e.target.value)}
            style={{
              minHeight: '44px',
              paddingRight: searchQuery ? '36px' : 'var(--ro-space-3)',
            }}
          />
          {searchQuery && (
            <button
              type="button"
              aria-label="Aramayı temizle"
              onClick={() => onSearchChange('')}
              style={{
                position: 'absolute',
                right: '8px',
                top: '50%',
                transform: 'translateY(-50%)',
                background: 'transparent',
                border: 'none',
                color: 'var(--ro-color-text-muted)',
                cursor: 'pointer',
                padding: 'var(--ro-space-1)',
                minWidth: '28px',
                minHeight: '28px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: 'var(--ro-font-size-md)',
              }}
            >
              ×
            </button>
          )}
        </div>

        <Button
          type="button"
          variant={activeFilterCount > 0 ? 'primary' : 'outline'}
          size="md"
          onClick={onOpenFilters}
          aria-label={
            activeFilterCount > 0
              ? `Filtreleri aç (${activeFilterCount} aktif)`
              : 'Filtreleri aç'
          }
          style={{
            minHeight: '44px',
            flexShrink: 0,
            display: 'inline-flex',
            alignItems: 'center',
            gap: 'var(--ro-space-2)',
          }}
          data-testid="open-filters-btn"
        >
          <span>Filtreler</span>
          {activeFilterCount > 0 && (
            <Badge variant="neutral" style={{ fontSize: '10px', padding: '1px 6px' }}>
              {activeFilterCount}
            </Badge>
          )}
        </Button>
      </div>

      {hasActiveFilters && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            fontSize: 'var(--ro-font-size-xs)',
            color: 'var(--ro-color-text-muted)',
            padding: 'var(--ro-space-1) var(--ro-space-2)',
            backgroundColor: 'var(--ro-color-surface-hover)',
            borderRadius: 'var(--ro-radius-md)',
          }}
        >
          <span>
            {activeFilterCount > 0
              ? `${activeFilterCount} filtre uygulandı`
              : 'Arama filtresi devrede'}
          </span>
          <button
            type="button"
            onClick={onClearFilters}
            data-testid="clear-all-filters-btn"
            style={{
              background: 'transparent',
              border: 'none',
              color: 'var(--ro-color-primary)',
              cursor: 'pointer',
              fontWeight: 'var(--ro-font-weight-semibold)',
              fontSize: 'var(--ro-font-size-xs)',
              textDecoration: 'underline',
              padding: 'var(--ro-space-1)',
              minHeight: '36px',
            }}
          >
            Filtreleri Temizle
          </button>
        </div>
      )}
    </div>
  );
};
