import React from 'react';

export interface CategoryItem {
  id: string;
  name: string;
  itemCount: number;
}

export interface MenuCategoryBarProps {
  categories: CategoryItem[];
  activeCategoryId: string | null;
  onSelectCategory: (categoryId: string | null) => void;
  totalItemCount: number;
}

export const MenuCategoryBar: React.FC<MenuCategoryBarProps> = ({
  categories,
  activeCategoryId,
  onSelectCategory,
  totalItemCount,
}) => {
  return (
    <nav
      aria-label="Menü Kategorileri"
      role="tablist"
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--ro-space-2)',
        overflowX: 'auto',
        padding: 'var(--ro-space-2) 0',
        scrollbarWidth: 'none',
        WebkitOverflowScrolling: 'touch',
      }}
      data-testid="menu-category-bar"
    >
      <button
        type="button"
        role="tab"
        aria-selected={activeCategoryId === null}
        data-testid="category-pill-all"
        onClick={() => onSelectCategory(null)}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 'var(--ro-space-1)',
          padding: 'var(--ro-space-2) var(--ro-space-3)',
          borderRadius: 'var(--ro-radius-full)',
          fontSize: 'var(--ro-font-size-sm)',
          fontWeight: activeCategoryId === null ? 'var(--ro-font-weight-semibold)' : 'var(--ro-font-weight-regular)',
          border: '1px solid',
          borderColor: activeCategoryId === null ? 'var(--ro-color-text-primary)' : 'var(--ro-color-border)',
          backgroundColor: activeCategoryId === null ? 'var(--ro-color-text-primary)' : 'var(--ro-color-surface)',
          color: activeCategoryId === null ? 'var(--ro-color-surface)' : 'var(--ro-color-text-primary)',
          cursor: 'pointer',
          whiteSpace: 'nowrap',
          minHeight: '44px',
          transition: 'all var(--ro-transition-fast)',
        }}
      >
        <span>Tümü</span>
        <span
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            opacity: 0.8,
          }}
        >
          ({totalItemCount})
        </span>
      </button>

      {categories.map((cat) => {
        const isSelected = activeCategoryId === cat.id;
        return (
          <button
            key={cat.id}
            type="button"
            role="tab"
            aria-selected={isSelected}
            data-testid={`category-pill-${cat.id}`}
            onClick={() => onSelectCategory(cat.id)}
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 'var(--ro-space-1)',
              padding: 'var(--ro-space-2) var(--ro-space-3)',
              borderRadius: 'var(--ro-radius-full)',
              fontSize: 'var(--ro-font-size-sm)',
              fontWeight: isSelected ? 'var(--ro-font-weight-semibold)' : 'var(--ro-font-weight-regular)',
              border: '1px solid',
              borderColor: isSelected ? 'var(--ro-color-text-primary)' : 'var(--ro-color-border)',
              backgroundColor: isSelected ? 'var(--ro-color-text-primary)' : 'var(--ro-color-surface)',
              color: isSelected ? 'var(--ro-color-surface)' : 'var(--ro-color-text-primary)',
              cursor: 'pointer',
              whiteSpace: 'nowrap',
              minHeight: '44px',
              transition: 'all var(--ro-transition-fast)',
            }}
          >
            <span>{cat.name}</span>
            <span
              style={{
                fontSize: 'var(--ro-font-size-xs)',
                opacity: 0.8,
              }}
            >
              ({cat.itemCount})
            </span>
          </button>
        );
      })}
    </nav>
  );
};
